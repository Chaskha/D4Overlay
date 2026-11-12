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
4. APK à partager : *Build → Build App Bundle(s) / APK(s) → Build APK(s)* → `app/build/outputs/apk/debug/app-debug.apk`.

Tests de la logique (parsing de l'API, cadences, alertes) : clic droit sur `app/src/test` → *Run tests*, ou `gradlew test` une fois le wrapper généré.

> Le fichier `gradle-wrapper.jar` n'est pas dans le dépôt (fichier binaire). Android Studio n'en a pas besoin. Pour la ligne de commande, le générer une fois avec `gradle wrapper` ou depuis Android Studio.

## Ressources partagées avec Windows (pas de copie dans le dépôt)

La tâche Gradle `copySharedAssets` prend à chaque build :

| Source (version Windows) | Dans l'APK |
|---|---|
| `Shared/Assets/Lang/*.json` (12 langues) | `assets/lang/` |
| `Packaging/defaults/sounds/*.mp3` (11 voix) | `assets/sounds/` |

Une traduction corrigée côté Windows l'est donc aussi sur Android. Les textes **propres au mobile** (18 clés `m.*`) sont dans `app/src/main/assets/lang-mobile/*.json`, **mêmes clés dans les 12 fichiers** (UTF-8 sans BOM, LF).

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
   ├─ TimersCard.kt           barre de titre (res/drawable-nodpi/title_bar.png) + carte des timers et prochaine alerte
   ├─ EventCard.kt            affichage / alarmes / paliers (minutes, son, volume, ▶ Tester)
   ├─ WebScheduleFetcher.kt   repli navigateur intégré (Cloudflare), appli ouverte seulement
   └─ Format.kt               heures et comptes à rebours
```

## Comportements et différences avec Windows

- **Source des données** : HTTP direct → navigateur intégré invisible (si Cloudflare bloque, **seulement appli ouverte**) → cache → cadence. Appli fermée, l'actualisation de fond ne fait que l'HTTP direct ; si elle échoue, les alertes continuent sur le cache puis la cadence.
- **Timer masqué** : sur Windows il ne sonne plus ; sur Android **ses alertes continuent** (sur téléphone, c'est la notification qui compte).
- **Repli navigateur** : la page du site interroge /api/schedule toutes les 4 s ; sans résultat au bout de 12 s, l'adresse de l'API est ouverte directement (méthode Windows). Sur l'émulateur, Cloudflare répond 403 deux fois puis laisse passer (~8 s).
- **Alertes** : mêmes paliers par défaut que Windows (8 / 4 / 2 min, Legion 4 / 2). Un palier dépassé à l'ouverture ou après un changement de réglage **ne sonne pas**. Plusieurs paliers dus en même temps (téléphone en veille) : un seul son, le plus proche. Une alerte en retard de plus de 10 min est abandonnée.
- **Alarmes exactes** : sur Android 14+, il faut les autoriser (bannière rouge + bouton). Sans elles, les alertes peuvent arriver quelques minutes en retard.
- **Son** : flux « notification » du téléphone, donc **respecte le mode silencieux** ; volume du palier en plus (courbe quadratique). Lecture coupée à **9 s** (limite d'Android pour un réveil en arrière-plan ; Windows : 20 s).
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
- Icône provisoire (sablier) : à remplacer par celle d'Arnaud (`res/drawable/ic_launcher_foreground.xml`, ou *New → Image Asset* dans Android Studio).
- Pas de signature de version « release » ni de publication Play Store : APK de debug seulement pour l'instant.
