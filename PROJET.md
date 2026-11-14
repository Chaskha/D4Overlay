# D4 Overlay — instructions du projet

Overlay Windows qui affiche les comptes à rebours des évènements de **Diablo IV** (World Boss, Helltide, Legion) et joue des alertes sonores avant leur début.
Ce document sert de **référence du projet** et de **passation** : il suffit de le lire (ou de le fournir à Claude) pour reprendre le travail sans rien oublier.

- Outil non officiel, réalisé par un fan, **non affilié à Blizzard Entertainment**.
- Projet orchestré par Arnaud ; **tout le code est écrit par Claude** (Arnaud teste, décide et distribue).
- Données : <https://helltides.com/api/schedule>.

---

## 1. Méthode de travail (à respecter)

### Rôles
| Qui | Fait quoi |
|---|---|
| **Arnaud** | Décide, compile (`dotnet build`), teste sur son PC, distribue. Ne code pas. |
| **Claude** | Écrit tout le code, les textes, les traductions, les scripts et la documentation. |

### Règles d'itération
1. **Une petite itération par tour**, pour que chacune puisse être annulée séparément. Ne pas empiler des fonctionnalités non demandées.
2. Quand un choix de conception existe, **proposer avant de coder** (en une question au plus), sauf si la demande est claire.
3. À chaque itération : livrer **toute la solution** dans un seul zip, `D4Overlay.zip`, régénéré depuis `/home/claude/build`.
4. **Annoncer en tête de message les fichiers ajoutés, supprimés ou renommés.** Extraire un zip par-dessus un dossier n'efface jamais rien : un fichier supprimé côté Claude reste chez Arnaud et peut casser le build (cas vécu : `HotkeysPanel.cs` renommé en `SettingsPanel.cs` a provoqué un doublon de `HotkeyBox`).
5. Terminer par **ce qui a changé, ce qui n'a pas été testé, et une checklist de test** courte.
6. Être honnête sur les limites : dire ce qui est vérifié, ce qui ne l'est pas, et ce que Claude ne sait pas faire (par exemple redessiner des illustrations).
7. Ton : tutoiement, français direct, sans jargon inutile.

### Ce que Claude peut et ne peut pas vérifier
- **Peut** : analyse de syntaxe C# (`pip install tree-sitter tree-sitter-c-sharp`), cohérence des 12 fichiers de langue, aperçus d'images avec PIL, tests numériques en Python.
- **Peut compiler et exécuter la logique pure** (sans Windows Forms) : le SDK .NET 8 s'installe dans le bac à sable avec `apt-get install dotnet-sdk-8.0` (dépôt Ubuntu autorisé). Hors ligne, NuGet doit être neutralisé avec un `nuget.config` (`<clear/>` + une source locale vide). C'est ainsi qu'a été validé `FrameCleanerCore.cs`, avec une maquette de `System.Drawing` pour la couche `Bitmap`.
- **Ne peut pas** : compiler la solution complète (WinForms, NAudio, WebView2 demandent NuGet et le pack Windows), lancer l'overlay, entendre un son, voir le rendu réel, ni tester l'installeur.
- Donc : **toujours** donner à Arnaud des étapes de test pour ce qui n'a pas pu être exécuté.

### Environnement de travail de Claude
- Sources dans `/home/claude/build` ; livrable dans `/mnt/user-data/outputs/D4Overlay.zip`.
- `/home/claude` peut être réinitialisé. Dans ce cas : `mkdir -p /home/claude/build && cd /home/claude/build && unzip -q -o /mnt/user-data/outputs/D4Overlay.zip`, puis réinstaller `tree-sitter` et `tree-sitter-c-sharp` (`pip install --break-system-packages`).
- Le zip se fabrique avec `shutil.make_archive('/mnt/user-data/outputs/D4Overlay', 'zip', '/home/claude/build')`.

---

## 2. Architecture

Trois projets C# (.NET 8, `net8.0-windows`, WinForms) dans `D4Overlay.sln` (écrit à la main).

```
D4Overlay/
├─ D4Overlay.sln
├─ publish.bat                    zip portable + installeur (voir §5)
├─ Shared/                        bibliothèque commune (NAudio 2.2.1)
│  ├─ AlertConfig.cs              alertes : niveaux (minutes, son, volume), options par évènement
│  ├─ ConfigStore.cs              chemins %AppData%, alerts.json, copie des fichiers par défaut, liste des cadres
│  ├─ AlertSounds.cs              bips intégrés + lecture de fichiers (wav, mp3, m4a, aac, wma, flac), sons de retour
│  ├─ OverlayLayout.cs            layout.json : police, positions, couleurs, cadre, échelle, opacité
│  ├─ OverlayRenderer.cs          rendu commun à l'overlay et à l'aperçu du configurateur
│  ├─ Skins.cs                    cadres intégrés (Métal par défaut, Lava) et leur profil par défaut
│  ├─ FrameCleaner.cs             nettoyage d'un cadre importé : couche Bitmap
│  ├─ FrameCleanerCore.cs         nettoyage : algorithme pur sur tableaux d'octets (testable seul)
│  ├─ FontCatalog.cs              polices (embarquées, dossier fonts, Windows)
│  ├─ Loc.cs                      localisation + AppSettings / SettingsStore (settings.json)
│  ├─ Hotkeys.cs                  raccourcis : modèle, analyse, règles
│  ├─ OverlayStatus.cs            status.json publié par l'overlay (lu par le configurateur)
│  ├─ OverlayMessages.cs          message Windows « recharge la configuration »
│  ├─ StartupRegistration.cs      démarrage avec Windows (HKCU\...\Run)
│  ├─ SingleInstance.cs, CrashLog.cs, AppLog.cs, AppInfo.cs
│  └─ Assets/                     cadres PNG + masques, Sounds/whoosh.wav, Fonts/, Lang/*.json
├─ Overlay/                       D4WorldBossOverlay.exe : fenêtre superposée, icône de notification, API
│  ├─ OverlayForm.cs, Program.cs
├─ Config/                        D4OverlayConfig.exe : fenêtre de configuration
│  ├─ ConfigForm.cs               fenêtre principale, enregistrement, retour « application en cours »
│  ├─ AppearancePanel.cs          cadre, police, couleurs, échelle, opacité, aperçu avec glisser
│  ├─ SettingsPanel.cs            onglet Configuration : affichage, démarrage, raccourcis
│  ├─ InfoPanel.cs                état, crédits, mentions légales, contact, journaux, langues
│  ├─ AppTheme.cs, TabHost.cs, WheelRouter.cs, Program.cs
├─ Installer/                     D4Overlay.iss (Inno Setup), LISEZMOI.txt
├─ Packaging/                     LISEZMOI.txt, README.txt, LICENSES.txt, defaults\ (copié chez l'utilisateur),
│                                 sounds-source\ et frames-source\ (originaux, jamais embarqués)
├─ Tools/clean_frame.py           même nettoyage de cadre que l'application (pour préparer des images)
└─ android/                       version Android (Kotlin + Compose) : timers + alertes par notification,
                                  voir android/LISEZMOI.md
```

Notes :
- Les icônes d'Arnaud (`Overlay/Assets/D4Overlay.ico`, `Config/Assets/D4Overlay_Config.ico`) sont référencées par `ApplicationIcon` dans **ses** `.csproj` d'Overlay et de Config, qu'il conserve. Ne lui fournir qu'un extrait de `.csproj` à fusionner, jamais un remplacement aveugle. Seul le `.csproj` de Shared change d'une itération à l'autre.
- Verrous d'instance unique : `D4Overlay.Overlay.SingleInstance` et `D4Overlay.Config.SingleInstance` (réutilisés par l'installeur via `AppMutex`).
- Messages Windows enregistrés : activation de la fenêtre existante, et `D4Overlay.Overlay.Reload` (le configurateur demande à l'overlay de relire sa configuration).
- **Android** : `android/` prend au build les langues et les voix dans `Shared/` et `Packaging/` (aucune copie). Une clé ajoutée aux langues Windows est donc disponible sur Android ; les textes propres au mobile sont dans `android/app/src/main/assets/lang-mobile/`. La logique (cadences, parsing de l'API, paliers) est **dupliquée** en Kotlin : un changement de cadence ou d'API est à reporter des deux côtés (`OverlayForm.cs` ↔ `android/.../data/`).

---

## 3. Données de l'utilisateur

Tout est dans `%AppData%\D4WorldBossOverlay\` (rien dans le dossier de l'application).

| Fichier / dossier | Contenu |
|---|---|
| `alerts.json` | Alertes par évènement (activée, visible, « seulement si Diablo IV tourne », niveaux). Écriture atomique. |
| `layout.json` | Police, taille, gras, espacement, positions des timers, couleurs, cadre, fond intérieur, `SkinProfiles`, échelle 50–300 %, opacité 20–100 %. |
| `settings.json` | `Language`, `OverlayVisible`, `Theme`, `GameProcesses` (par défaut `["Diablo IV"]`), `Hotkeys`, `StartWithWindows`. |
| `status.json` | Publié par l'overlay : PID, source, mode déplacement, visibilité, sourdine, jeu détecté, conflits de raccourcis, dernière réussite de l'API, `ConfigAppliedAt`. |
| `skins\`, `sounds\`, `fonts\`, `lang\` | Ressources ajoutées par l'utilisateur ou copiées depuis `defaults\`. |
| `defaults-seeded.txt` | Liste des fichiers par défaut déjà proposés (voir §6). |
| `overlay.log`, `shared.log`, `crash.log` | Journaux. |

---

## 4. Comportements à connaître

### API et cadences
- Interrogation toutes les **10 minutes**, tick de 1 s. HTTP direct d'abord, repli WebView2 (navigation) ensuite, repli sur un calcul local si tout échoue. Le statut affiché est honnête (API, cache ou cadence).
- Cadences de repli codées en dur (peuvent dériver) : **World Boss** toutes les 3 h 30, ancrage 2024-01-01 08:30 UTC ; **Legion** toutes les 25 min, ancrage 2026-10-26 05:40 UTC.
- **À surveiller** : à la prochaine saison, la Helltide n'aura plus de minuteur mais une rotation de régions. helltides.com changera probablement son API : la ligne Helltide, les alertes et la cadence de repli seront à revoir.

### Alertes et sons
- Paliers « minutes avant » : son, volume (courbe quadratique), test, import. Bips intégrés synthétisés (`asterisk`, `exclamation`, `hand`, `question`, `beep`) et fichiers **wav / mp3 / m4a / aac / wma / flac** (les formats hors wav et mp3 passent par Media Foundation).
- Lecture d'un fichier **coupée à 20 s**.
- Sourdine rapide depuis l'icône de notification : pour la session seulement, non enregistrée ; les paliers franchis pendant la sourdine sont consommés en silence.
- Sons de retour (mute / unmute) : whoosh intégré, à l'envers pour couper, 60 % du volume, repli sur un bip discret.
- 11 voix d'annonce (`WB-*`, `HT-*`, `Legion-*`) livrées dans `Packaging\defaults\sounds`, **égalisées à environ −19 LUFS** (plafond −1,5 dB). Elles ne sont assignées à aucun palier par défaut.

### Fenêtre de l'overlay
- Fenêtre sans bordure à alpha par pixel (`UpdateLayeredWindow`), transparente aux clics sauf en mode déplacement. Replacement automatique si hors écran (démarrage, F8, changement d'écran).
- Échelle 50–300 %, opacité globale 20–100 % (forcée à 100 % en mode déplacement).
- Option « seulement si Diablo IV tourne » (processus mis en cache 5 s, liste modifiable dans `settings.json`).
- Icône de notification : afficher/masquer, sourdine, configuration, quitter. Overlay masquable : il reste alors les alertes sonores.

### Cadres (skins)
- Intégrés : **Métal sobre (par défaut)** et **Lava**. Chacun a un profil par défaut (taille de police et positions) et se rend avec un **fond intérieur réglable** (couleur, opacité 0,8 par défaut) posé sous le cadre grâce à un masque.
- Défauts du Métal : police **Comic Sans MS 12, espacement 0**, positions World Boss (40, 64), Helltide (40, 106), Legion (40, 85). Lava : 16 px, (50, 84), (50, 112), (50, 140).
- **Import d'une image** : `FrameCleaner` corrige automatiquement un voile semi-transparent autour du cadre, un fond intérieur « cuit » dans l'image et les petites poussières. Une image déjà propre n'est pas modifiée. Le fichier d'origine n'est jamais touché. Seuils et garde-fous : voir `FrameCleanerCore.cs` ; `Tools/clean_frame.py` doit **rester strictement synchronisé** (comparaisons en entiers sur l'alpha).
- Image personnalisée : la zone intérieure est détectée automatiquement (cadre fermé requis, sinon pas de fond).

### Fenêtre de configuration
- Onglets : un par évènement, Apparence, **Configuration** (affichage, démarrage avec Windows, raccourcis), Infos.
- Thème système / clair / sombre, suivi en direct. Reconstruction à chaud (langue, thème) sans perdre l'état non enregistré.
- **Enregistrer** : le configurateur signale l'overlay (message `Reload`), puis affiche « Application des changements à l'overlay… » (clignotant) jusqu'à ce que l'overlay publie `ConfigAppliedAt` ; confirmation verte 2 s ; repli après 6 s. Sans overlay lancé, le message le dit. Le contrôle périodique (10 s) et F5 restent en filet de sécurité.
- **Molette** (`WheelRouter`) : elle agit sur ce qui est sous la souris ; un champ (liste, nombre, curseur) ne la reçoit que s'il a le focus par un clic.
- Panneaux défilants : un espace de 16 px en bas évite de rogner le dernier bouton.

### Raccourcis
- Par défaut : F8 mode déplacement, F5 actualiser, sourdine aucun, PageUp / PageDown opacité ±5 % (**enregistrés seulement en mode déplacement**). Sondés au démarrage pour détecter les conflits.
- Règles : touche de fonction, navigation ou pavé numérique seul, ou toute touche avec Ctrl / Alt. Doublons bloqués à l'enregistrement. Un conflit détecté par l'overlay s'affiche dans le configurateur.

### Démarrage avec Windows
- Entrée `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (aucun droit administrateur). **L'overlay s'inscrit lui-même** avec son propre chemin, à chaque lancement et à chaque changement de réglage, ce qui répare un dossier déplacé. Jamais d'inscription depuis `dotnet.exe`.
- Si Windows a désactivé l'entrée (Gestionnaire des tâches), un avertissement rouge s'affiche dans l'onglet.
- Réglage de référence : `StartWithWindows` dans `settings.json` (sinon l'overlay supprimerait l'entrée).

### Langues
- 12 langues : `en` (défaut), `fr`, `it`, `pt`, `es`, `de`, `zh`, `ko`, `ja`, `ru`, `tr`, `pl`. Fichiers `Shared/Assets/Lang/*.json`, clés → textes, **mêmes clés dans les 12** (vérifier à chaque ajout).
- L'utilisateur peut ajouter une langue ou corriger une langue intégrée : bouton « Dossier des langues » → `_template.json`, `_reference\` (une copie de chaque langue intégrée), `README.txt`. Un fichier utilisateur peut ne contenir que les lignes à changer ; le reste reprend le texte intégré.
- Les textes contenant des noms de l'interface utilisent des paramètres `{0}` (jamais de nom de touche ou de libellé écrit en dur dans une traduction).

---

## 5. Build et distribution

### Compilation
```
dotnet build D4Overlay.sln -c Release
```
Objectif : **0 avertissement**. (Le conflit `MSB3277` vient de la DLL WPF du package WebView2 : une cible dans le `.csproj` de l'overlay la retire avant la résolution des références.)

### Fabrication des livrables
```
publish.bat            → zip portable + installeur
publish.bat zip        → zip portable seul (.NET 8 Desktop Runtime requis)
publish.bat setup      → installeur seul (.NET embarqué)
```
Résultat dans `dist\` : `D4Overlay-portable-<version>.zip` et `D4Overlay-Setup-<version>.exe`.
Prérequis de l'installeur : **Inno Setup 6 ou 7** (gratuit pour un usage non commercial ; licence à acheter pour un usage commercial). Variable `ISCC_PATH` si installé hors des dossiers habituels.

### Règles de version et d'identité
- La version se change à **deux endroits** : `Shared/AppInfo.cs` et la ligne `set VERSION=` de `publish.bat` (le script avertit s'ils diffèrent).
- L'**`AppId` (GUID) de `Installer/D4Overlay.iss` ne doit jamais changer** : il permet les mises à jour sur place.

### Installeur (Inno Setup)
- Installation **par utilisateur** (pas de fenêtre d'administrateur), langue de l'assistant reprise pour régler la langue de l'appli à la première installation (jamais d'écrasement d'un réglage existant).
- Cases : icône de bureau, démarrage avec Windows. Page d'information avant l'installation = `LICENSES.txt`. Détection de l'overlay en cours d'exécution (verrous d'instance).
- Désinstallation : supprime les fichiers et l'entrée de démarrage ; demande s'il faut supprimer aussi les réglages (« non » par défaut).
- Les langues de l'assistant ne se chargent que si leur `.isl` existe (coréen et chinois : traductions non officielles).

### Encodages à respecter
| Fichier | Encodage |
|---|---|
| `publish.bat`, `Packaging/*.txt` | ASCII pur, fins de ligne CRLF |
| `Installer/D4Overlay.iss` | UTF-8 **avec BOM**, CRLF |
| `Shared/Assets/Lang/*.json` | UTF-8 sans BOM, LF |

---

## 6. Pièges et décisions à ne pas défaire

- **Un zip n'efface rien** : toujours lister les fichiers supprimés ou renommés. Fichiers qui n'ont plus de raison d'exister : `Config/HotkeysPanel.cs`, `Shared/EmbeddedFonts.cs`.
- **`EnsureDefaults` copie une seule fois** : `defaults-seeded.txt` mémorise ce qui a été proposé. Un fichier supprimé par l'utilisateur ne revient pas ; un nouveau fichier d'une version future est copié. On n'écrase jamais un fichier existant.
- **Les cadres embarqués ont un intérieur transparent** + un masque (`interior-fill*.png`). Ne pas réintroduire un fond « cuit » dans l'image : le réglage de fond ne marcherait plus.
- **Le cadre Lava reste tel quel** (ses icônes d'ordre crâne / bannière / flamme ne correspondent pas à l'ordre des timers ; décision d'Arnaud : on n'y touche pas pour l'instant). Le Métal est le cadre par défaut.
- **Cadre mémorisé** : une valeur vide dans `layout.json` veut dire « Lava » (historique). Les installations existantes gardent donc leur cadre ; seules les nouvelles installations et « Réinitialiser l'apparence » passent au Métal. Si le `layout.json` de `Packaging\defaults` est copié depuis une configuration Lava, il impose le Lava.
- **Seuils du nettoyage de cadre** en entiers (alpha sur 255) dans le C# et le Python : la comparaison en flottants donnait des résultats différents sur les pixels à la limite.
- **La molette** est aiguillée par `WheelRouter` (filtre de messages). Ne pas la remplacer par un comportement au focus.
- **Une image mal détourée** (voile à 20–40 %) provient d'IA ou de GIMP : ne pas demander à l'utilisateur de la corriger à la main, l'application et `clean_frame.py` le font.
- **Polices** : aucune n'est embarquée (Comic Sans MS et Segoe UI viennent de Windows). La police `DIABLO_L.TTF` fournie par Arnaud a une licence floue : non distribuée.
- **Pas d'alerte « tout de suite »** : Arnaud l'a refusée.
- **Fuites de réglages** : si le configurateur est ouvert pendant un changement d'opacité par raccourci, « Enregistrer » écrase `layout.json` (dernier qui écrit gagne). Limite connue.

---

## 7. État actuel et feuille de route

### Fait et validé par Arnaud
Correctif de l'API ; configurateur à onglets ; alertes par palier ; cadres avec alpha par pixel ; polices ; 12 langues ; thème ; échelle et opacité ; masquage de l'overlay ; instance unique ; journal de plantage ; raccourcis configurables ; démarrage avec Windows ; molette ; sons mp3 ; dossier des langues exploitable ; application immédiate des réglages avec confirmation ; Métal par défaut ; installeur Inno Setup (script écrit).

### Android (détails : `android/LISEZMOI.md`)
- **Version 1.0.1 validée par Arnaud sur Samsung S20** : timers, alertes par notification appli fermée (une seule alarme exacte), timers masqués qui sonnent quand même, repli WebView pour l'API (Cloudflare bloque l'appel direct), respect de « Ne pas déranger », bannières de classe, sons des alertes simultanées joués à la suite.
- Distribution : **APK signé sur GitHub Releases** (la clé de signature et son mot de passe restent chez Arnaud, jamais dans le dépôt). Play Store envisagé plus tard.
- Limite connue : appli fermée, l'actualisation en arrière-plan passe par HTTP direct, bloqué par Cloudflare ; l'appli vit alors sur le cache puis la cadence. Contact pris avec helltides.com.

### Livré, en attente de test d'Arnaud
- Nettoyage automatique des cadres importés (`FrameCleaner`) et note « image nettoyée ».
- `LICENSES.txt` et bloc Mentions légales de l'onglet Infos ; page d'information de l'installeur.
- Réglages par défaut d'un ami (Comic Sans MS 12, positions) et 11 voix embarquées.
- **L'installeur n'a jamais été exécuté** : test sur un Windows vierge à faire (fichier `D4Overlay-Sandbox.wsb` fourni pour Windows Sandbox).

### À décider ou vérifier avant une vraie distribution
1. **Conditions de D4 Overlay** : `LICENSES.txt` dit seulement « gratuit, tel quel, sans garantie ». Choisir une vraie licence si besoin.
2. **helltides.com** : confirmer l'accord pour l'usage de l'API et la mention « powered by HellTides.com » présente dans les cadres.
3. **Licences des sons** : le whoosh dérivé de « SWSH Whoosh 2 (ID 0573) » (La Sonothèque) ; l'origine et les conditions des 11 voix.
4. **Traductions** : relecture par des natifs (surtout les noms d'évènements : Helltide en polonais « Piekielny Przypływ », en turc « Cehennem Gelgiti »).
5. **Signature de code** : décision actuelle = distribuer d'abord **sans signature** (avertissement SmartScreen, expliqué dans les LISEZMOI), puis envisager un certificat OV si le public grandit. Pour un particulier en Belgique, le service cloud de signature de Microsoft est limité aux États-Unis et au Canada (à revérifier le moment venu).
6. Cadres haute résolution pour les écrans 4K (les originaux sont à fournir par Arnaud).
7. Mises à jour automatiques (piste : Velopack) : plus tard.

### Idées en attente (non demandées)
- Assigner par défaut les voix aux paliers (par exemple `WB-male`, `Legion-male`, `HT-male` sur le palier le plus proche).
- Refaire les cadres avec des icônes régulières (Arnaud les retravaille lui-même et les enverra).

---

## 8. Checklists de test (pour Arnaud)

**Installation** : sur un Windows vierge, lancer `D4Overlay-Setup-<version>.exe` ; vérifier l'absence de fenêtre d'administrateur, la page d'information, les raccourcis, le premier lancement dans la bonne langue, la réinstallation par-dessus (réglages conservés), la désinstallation (les deux réponses à la question sur les réglages).

**Enregistrement** : changer une touche, enregistrer, la remettre, enregistrer : le message clignotant puis vert doit apparaître et la touche marcher aussitôt ; fermer l'overlay et enregistrer : message « n'est pas lancé ».

**Démarrage avec Windows** : cocher, enregistrer, vérifier l'entrée dans le Gestionnaire des tâches, redémarrer ; désactiver l'entrée dans le Gestionnaire : l'avertissement rouge apparaît.

**Cadres** : importer un PNG avec halo semi-transparent : la note « ✓ Image nettoyée… » apparaît et le halo disparaît ; importer les originaux de `Packaging\frames-source\` : même résultat que les cadres embarqués.

**Sons** : importer un `.mp3`, l'assigner à un palier, ▶ Tester ; supprimer une voix de `%AppData%\...\sounds` et relancer : elle ne revient pas.

**Langues** : « Dossier des langues » → `_template.json`, `README.txt`, `_reference\` (12 fichiers) ; copier `fr.json` à côté, changer une ligne, rechoisir la langue.

**Molette** : cliquer dans une liste, passer la souris sur un panneau et faire défiler : le panneau défile, la liste ne bouge pas.

**Build** : `dotnet build` sans avertissement ; `publish.bat` produit le zip et l'installeur.