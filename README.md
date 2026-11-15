# D4Overlay
Diablo IV displays event timers as overlay with configurable alarms
REQUIREMENTS
- Windows 10 or 11 (64-bit)
- .NET 8 "Desktop Runtime" (x64): https://dotnet.microsoft.com/download/dotnet/8.0
  (not needed with the "self-contained" build of this package)
- Microsoft Edge WebView2 Runtime: already on Windows 11 and up-to-date Windows 10.
  It is only a fallback: the overlay works without it.

USAGE
1. Run D4WorldBossOverlay.exe: the frame appears on top of the game
   (the game must run in "borderless windowed" mode).
2. Run D4OverlayConfig.exe to set everything up: alerts (timings, sounds, volume),
   displayed events, frame image, font, colors, timer positions.
   The overlay picks up changes within 10 seconds.

START WITH WINDOWS
Settings tab, "Launch the overlay when Windows starts". Only the overlay starts (not the settings window).
Combined with "Show the overlay on screen" unchecked, it starts silently with just its icon near the clock.
If you move the application folder, start the overlay once from its new location: it fixes the startup entry itself.

LEGAL
D4 Overlay is an unofficial fan-made tool, not affiliated with Blizzard Entertainment. Diablo and Blizzard are trademarks
of Blizzard Entertainment, Inc. Licenses and notices: LICENSES.txt (also in the Info tab).

OVERLAY SIZE
Appearance tab, "Overlay size (%)": 50 to 300 %. Raise it on high-resolution (4K) screens.

OVERLAY OPACITY
Appearance tab, "Overlay opacity (%)": 20 to 100 %. In move mode (F8) the overlay is always fully opaque.

ALERTS ONLY WHILE THE GAME RUNS
In each event tab, check "Only while Diablo IV is running" so that this event's alerts stay silent when the game is closed.
Leave it unchecked to be alerted anyway (e.g. a World Boss alert 5 minutes ahead, to give you time to launch the game).
The Info tab shows whether the game is detected. If the game's executable has another name, edit "GameProcesses" in settings.json.

QUICK MUTE
Right-click the icon near the clock and choose "Mute sound alerts" (voice call, stream...). Nothing else changes.
It is not remembered: the sounds come back the next time the overlay starts.

SOUND ALERTS ONLY
Uncheck "Show the overlay on screen" in the Settings tab: nothing is drawn, the sound alerts
keep working. The icon near the clock (notification area) lets you show the overlay again,
open the settings or quit the application.

SHORTCUTS (in game) - all configurable in the "Settings" tab
- F8: move mode (dashed frame). Drag the window, then press F8 again to lock it.
      In move mode, the red cross at the top-right closes the application.
- F5: force a refresh of the data and of the settings.
- Mute / unmute the sound alerts: no key by default (assign one if you want).
- PageUp / PageDown: overlay opacity + / -, ONLY in move mode (so no extra key is taken while you play).
A shortcut is global: while the overlay runs, other programs can no longer use that key.
If Windows refuses a key (already used by another application), the tab says so.

WINDOW THEME
Bottom right of the settings window: System (follows the Windows light/dark mode), Light or Dark.

LANGUAGE
English by default. Use the globe selector at the bottom right of the settings window:
English, French, Italian, Portuguese, Spanish, German, Polish, Turkish, Simplified Chinese,
Korean, Japanese and Russian are included. To add or fix a language, see the "Info" tab.

DATA AND SETTINGS
Everything lives in %AppData%\D4WorldBossOverlay:
  alerts.json (alerts), layout.json (appearance), settings.json (language),
  skins\ (images), sounds\ (sounds: wav, mp3, m4a, aac, wma, flac), fonts\ (custom .ttf / .otf fonts), lang\ (added languages),
  defaults-seeded.txt (default files already offered), overlay.log, shared.log and crash.log (logs, useful when something goes wrong).
To reset everything: close the overlay and delete that folder.

UNINSTALL
Delete the application folder and the %AppData%\D4WorldBossOverlay folder.

ANDROID VERSION
"D4 Timers", a companion app for Android 8.0 and later: the same timers, plus sound alerts by notification, even when the app is closed.
Download the APK (D4Timers-<version>-release.apk) from the Releases page: https://github.com/Chaskha/D4Overlay/releases
On the phone, open the file and allow installing apps from this source when Android asks.
On first launch, allow notifications and exact alarms (needed for the alerts to ring on time).
"Do Not Disturb" and silent mode are respected: the notification shows, without sound.
Details (French): android/LISEZMOI.md

PROJECT NOTES
Architecture, decisions and working method (French): PROJET.md

Event data comes from helltides.com.
Mute / unmute feedback sound: derived from "SWSH Whoosh 2 (ID 0573)", La Sonotheque.
