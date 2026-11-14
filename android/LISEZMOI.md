# D4 Overlay — version Android

Application compagnon Android : les trois comptes à rebours (World Boss, Legion, Helltide) sous une barre de titre « Diablo IV Events », et des **alertes par notification + son** X minutes avant chaque évènement, même quand l'appli est fermée.
Diablo IV ne tourne pas sur téléphone : il n'y a donc pas d'overlay par-dessus le jeu.

Kotlin + Jetpack Compose, Android 8.0 (API 26) minimum, cible Android 15 (API 35).

---

## Compiler et installer

Prérequis : **Android Studio** (récent, Ladybug ou plus). Il apporte le SDK Android et le JDK 17 nécessaires.

1. Android Studio → *Open* → choisir le dossier `android/` (pas la racine du dépôt).
2. Laisser la synchronisation Gradle se terminer (premier lancement : téléchargement de Gradle 8.10.2 et des dépendances).
3. Brancher le téléphone en mode développeur (débogage USB) ou lancer un émulateur, puis ▶ *Run 'app'*.
4. APK à partager : voir *Distribuer (APK signé)* ci-dessous. L'APK de debug ne sert qu'aux tests.

## Distribuer (APK signé, GitHub Releases)

**Une seule fois : la clé de signature.** Toutes les versions doivent être signées avec **la même clé**, sinon les utilisateurs devront désinstaller pour mettre à jour.

1. Android Studio → *Build → Generate Signed App Bundle / APK…* → **APK** → *Next*.
2. *Key store path* → *Create new…* :
   - emplacement **hors du dépôt**, par exemple `D:\DEV\keys\d4overlay.jks` ;
   - un mot de passe solide (le même pour *Password* et la clé, c'est plus simple) ;
   - *Alias* : `d4overlay`, *Validity* : 25 ans (ou plus), *First and Last Name* : Arnaud (ou Chaskha) ; le reste est facultatif.
3. **Sauvegarder** le fichier `.jks` + le mot de passe ailleurs (clé USB, gestionnaire de mots de passe). Sans eux, plus de mise à jour possible.

**À chaque version :**

1. Dans `app/build.gradle.kts` : augmenter `versionCode` de 1 (2, 3…) et changer `versionName` (« 1.0.1 »…). Android refuse une mise à jour dont le `versionCode` n'augmente pas.
2. *Build → Generate Signed App Bundle / APK…* → **APK** → choisir la clé (Android Studio la propose déjà) → *Next* → variante **release** → *Create*.
3. Le fichier est dans `android/app/release/D4Overlay-<version>-release.apk`.
4. GitHub → dépôt → *Releases* → *Draft a new release* → tag `android-v1.0.0` → glisser l'APK → *Publish*.

**Côté utilisateur :** télécharger l'APK depuis le téléphone, l'ouvrir, autoriser le navigateur à « installer des applis inconnues ». Play Protect peut afficher un avertissement (appli inconnue de Google) : *Installer quand même*.

Tests de la logique (parsing de l'API, cadences, alertes) : clic droit sur `app/src/test` → *Run tests*, ou `gradlew test` une fois le wrapper généré.

> Le fichier `gradle-wrapper.jar` n'est pas dans le dépôt (fichier binaire). Android Studio n'en a pas besoin. Pour la ligne de commande, le générer une fois avec `gradle wrapper` ou depuis Android Studio.

## Ressources partagées avec Windows (pas de copie dans le dépôt)

La tâche Gradle `copySharedAssets` prend à chaque build :

| Source (version Windows) | Dans l'APK |
|---|---|
| `Shared/Assets/Lang/*.json` (12 langues) | `assets/lang/` |
| `Packaging/defaults/sounds/*.mp3` (11 voix) | `assets/sounds/` |

Une traduction corrigée côté Windows l'est donc aussi sur Android. Les textes **propres au mobile** (19 clés `m.*`) sont dans `app/src/main/assets/lang-mobile/*.json`, **mêmes clés dans les 12 fichiers** (UTF-8 sans BOM, LF).

## Bannières d'en-tête

Les originaux d'Arnaud (`app/assets/`, jusqu'à 3900 px, 45 Mo) **ne sont pas dans le build ni dans git**. L'appli embarque des versions réduites dans `app/src/main/assets/banners/` : bords transparents rognés (en ignorant les 8 px du bord de la toile, où le détourage laisse parfois un liseré), 1200 px de large. `metal.png` (la barre d'origine) est la bannière par défaut.

Ajouter une bannière : déposer un PNG à fond transparent de ~1200 px de large dans `app/src/main/assets/banners/` ; elle apparaît automatiquement dans les réglages.

## Architecture

```
android/app/src/main/java/com/chaskha/d4overlay/
├─ D4App.kt                   démarrage : langue, canal de notification, actualisation périodique
├─ data/
│  ├─ Events.kt               les 3 évènements (clés API, fenêtre active, couleur, position dans le cadre)
│  ├─ ScheduleParser.kt       lecture de /api/schedule (portage de OverlayForm.TryApplyJson)
│  ├─ Cadence.kt              cadences de repli (mêmes ancrages que Windows)
│  ├─ Schedule.kt             API + cadence au-delà de la dernière entrée de l'API
│  ├─ AlertConfig.kt          paliers ; lecture/écriture au format alerts.json de Windows
│  ├─ AlertPlanner.kt         calcul des alertes à jouer (logique pure, testée)
│  ├─ ScheduleRepository.kt   HTTP direct, cache, choix de la source affichée
│  └─ Store.kt                fichiers et préférences (stockage privé de l'appli)
├─ alerts/
│  ├─ AlertScheduler.kt       programme UNE alarme système : la prochaine alerte
│  ├─ AlertReceiver.kt        réveil : notification + son, puis alarme suivante
│  ├─ SoundPlayer.kt          bips synthétisés (mêmes que Windows) et voix mp3
│  ├─ Notifications.kt        canal « Alertes des évènements » (sans son : l'appli joue le sien)
│  ├─ RefreshWorker.kt        actualisation toutes les ~30 min, appli fermée (WorkManager)
│  └─ SystemEventsReceiver.kt redémarrage, mise à jour, changement d'heure → reprogrammation
├─ i18n/Loc.kt                langues (fichiers de Windows + lang-mobile)
└─ ui/
   ├─ MainActivity.kt         écran unique : cadre, statut, permissions, évènements, réglages, à propos
   ├─ Banners.kt              bannière d'en-tête (assets/banners/*.png) + choix par miniatures dans les réglages
   ├─ TimersCard.kt           carte des timers et prochaine alerte
   ├─ EventCard.kt            affichage / alarmes / paliers (minutes, son, volume, ▶ Tester)
   ├─ WebScheduleFetcher.kt   repli navigateur intégré (Cloudflare), appli ouverte seulement
   └─ Format.kt               heures et comptes à rebours
```

## Comportements et différences avec Windows

- **Source des données** : HTTP direct → navigateur intégré invisible (si Cloudflare bloque, **seulement appli ouverte**) → cache → cadence. Appli fermée, l'actualisation de fond ne fait que l'HTTP direct ; si elle échoue, les alertes continuent sur le cache puis la cadence.
- **Timer masqué** : sur Windows il ne sonne plus ; sur Android **ses alertes continuent** (sur téléphone, c'est la notification qui compte).
- **Repli navigateur** : la page du site interroge /api/schedule toutes les 4 s ; sans résultat au bout de 12 s, l'adresse de l'API est ouverte directement (méthode Windows). Sur l'émulateur, Cloudflare répond 403 deux fois puis laisse passer (~8 s).
- **Alertes** : mêmes paliers par défaut que Windows (8 / 4 / 2 min, Legion 4 / 2). Un palier dépassé à l'ouverture ou après un changement de réglage **ne sonne pas**. Plusieurs paliers dus en même temps (téléphone en veille) : un seul son, le plus proche. Plusieurs évènements en même temps (ex. World Boss et Helltide à la même heure) : leurs sons sont joués **à la suite**, du plus proche au plus lointain (au plus ~9 s en tout, limite d'Android pour un réveil en arrière-plan). Une alerte en retard de plus de 10 min est abandonnée.
- **Alarmes exactes** : sur Android 14+, il faut les autoriser (bannière rouge + bouton). Sans elles, les alertes peuvent arriver quelques minutes en retard.
- **Son** : flux « notification » du téléphone, **pas de son en vibreur, silencieux ou « Ne pas déranger »** (la notification suit les règles d'Android). Pour être réveillé quand même : Réglages Android → Notifications de l'appli → canal « Alertes des évènements » → *Ignorer Ne pas déranger*. ▶ Tester joue toujours ; volume du palier en plus (courbe quadratique). Lecture coupée à **9 s** (limite d'Android pour un réveil en arrière-plan ; Windows : 20 s).
- **Pas porté** (sans objet sur téléphone) : déplacement / échelle / opacité de l'overlay, raccourcis clavier, « seulement si Diablo IV tourne » (l'option est conservée dans alerts.json mais ignorée), démarrage avec Windows, import de cadres, de polices et de sons, sourdine rapide.
- **Affichage** : pas de cadre d'overlay sur mobile. Barre de titre propre à Android, puis une carte arrondie : une ligne par timer (ordre World Boss, Legion, Helltide), « Prochaine alerte » et *Actualiser* en bas de la carte, source des données dans *À propos*.
- **Police** : celle du système en gras (Comic Sans MS n'existe pas sur Android).
- `alerts.json` a **le même format** que sur Windows.

## Checklist de test (Arnaud)

1. **Premier lancement** : la demande d'autorisation des notifications apparaît ; accepter. Si la bannière « alarmes exactes » s'affiche : *Autoriser* → activer → revenir : la bannière disparaît.
2. **Timers** : sous la barre de titre, les trois comptes à rebours défilent dans leur carte et correspondent à l'overlay Windows (même seconde). Le nom du World Boss s'affiche.
3. **Source** : la ligne « Source des données » indique l'API avec une heure. Mode avion + *Actualiser* : rien ne casse, la source passe à cache ou cadence.
4. **Alerte réelle** : sur la Legion (toutes les 25 min), mettre un palier à quelques minutes, fermer l'appli, verrouiller l'écran : notification + son à l'heure. Tester aussi avec une voix (`Legion-male`).
5. **▶ Tester** sur chaque son intégré et sur une voix ; changer le volume : la différence s'entend.
6. **Masquer** un évènement : il disparaît du cadre mais **sonne toujours**. **Désactiver ses alarmes** : il ne sonne plus.
7. **Redémarrer le téléphone** sans ouvrir l'appli : l'alerte suivante arrive quand même.
8. **Langue** : passer en anglais, en japonais, puis « Langue du système ».
9. **Valeurs par défaut** : confirmer, les paliers 8 / 4 / 2 reviennent.

## Non vérifié (à faire)

- Le projet **n'a jamais été compilé** : il a été écrit sans SDK Android. Les erreurs de la première synchronisation Gradle / compilation sont à renvoyer à Claude telles quelles.
- Passage de la vérification Cloudflare par le WebView : à confirmer sur un vrai téléphone.
- Icône : celle d'Arnaud (crâne et sablier) en version mobile, sans anneau ni étoiles, motif à 90 % pour rester dans la zone visible des découpes Android. Fichiers : `res/mipmap-xxxhdpi/ic_launcher_foreground.png` (432×432, fond `#4D101F`), `ic_launcher_monochrome.png` (icônes teintées d'Android 13+), `res/drawable/ic_notification.xml` (sablier blanc de la barre d'état).
- Pas de publication Play Store pour l'instant (APK signé sur GitHub). Avant le Play Store : nom et icône sans la marque Diablo, cible Android à remonter, test fermé de 14 jours.
