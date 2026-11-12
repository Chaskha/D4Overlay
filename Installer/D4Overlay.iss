; ============================================================================
;  D4 Overlay - installeur (Inno Setup 6 ou 7)
;
;  Ne se lance pas a la main : publish.bat publie l'application en version "autonome"
;  (.NET embarque) dans dist\setup-src\D4Overlay, puis appelle ISCC.exe sur ce fichier.
;  Resultat : dist\D4Overlay-Setup-<version>.exe
;
;  - installation PAR UTILISATEUR (pas de fenetre d'administrateur) ;
;  - les reglages restent dans %AppData%\D4WorldBossOverlay : une mise a jour ne les touche jamais ;
;  - les langues de l'assistant ne sont chargees que si leur fichier .isl existe dans votre
;    installation d'Inno Setup (le coreen et le chinois sont des traductions non officielles).
; ============================================================================

#define AppName "D4 Overlay"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppPublisher "Arnaud"
#define OverlayExe "D4WorldBossOverlay.exe"
#define ConfigExe "D4OverlayConfig.exe"
#define SourceDir "..\dist\setup-src\D4Overlay"

[Setup]
; Identifiant unique de l'application : NE JAMAIS LE CHANGER (c'est lui qui permet les mises a jour sur place).
AppId={{FFB5CAB1-D815-4E44-AFD7-2B974223603A}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppContact=chasha_claude@gmail.com
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=D4Overlay-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#OverlayExe}
; L'overlay et le configurateur utilisent ces verrous (instance unique) : l'installeur detecte qu'ils tournent
; et propose de les fermer au lieu d'echouer sur des fichiers verrouilles.
AppMutex=D4Overlay.Overlay.SingleInstance,D4Overlay.Config.SingleInstance
SetupMutex=D4OverlaySetupMutex
; Page d'information avant l'installation : non-affiliation a Blizzard et licences (le meme fichier est installe a cote du programme).
InfoBeforeFile=..\Packaging\LICENSES.txt
RestartApplications=no
#if FileExists(SourcePath + "..\Overlay\Assets\D4Overlay.ico")
SetupIconFile=..\Overlay\Assets\D4Overlay.ico
#endif

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
en.StartupTask=Start the overlay when Windows starts
en.DeleteData=Also delete your settings, images, sounds and logs (the D4WorldBossOverlay folder in AppData)?

#if FileExists(CompilerPath + "Languages\French.isl")
[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
[CustomMessages]
fr.StartupTask=Lancer l'overlay au démarrage de Windows
fr.DeleteData=Supprimer aussi vos réglages, images, sons et journaux (le dossier D4WorldBossOverlay dans AppData) ?
#endif

#if FileExists(CompilerPath + "Languages\Italian.isl")
[Languages]
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"
[CustomMessages]
it.StartupTask=Avvia l'overlay all'avvio di Windows
it.DeleteData=Eliminare anche impostazioni, immagini, suoni e registri (la cartella D4WorldBossOverlay in AppData)?
#endif

#if FileExists(CompilerPath + "Languages\BrazilianPortuguese.isl")
[Languages]
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
[CustomMessages]
pt.StartupTask=Iniciar o overlay com o Windows
pt.DeleteData=Excluir também suas configurações, imagens, sons e logs (a pasta D4WorldBossOverlay em AppData)?
#endif

#if FileExists(CompilerPath + "Languages\Spanish.isl")
[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
[CustomMessages]
es.StartupTask=Iniciar el overlay con Windows
es.DeleteData=¿Eliminar también sus ajustes, imágenes, sonidos y registros (la carpeta D4WorldBossOverlay en AppData)?
#endif

#if FileExists(CompilerPath + "Languages\German.isl")
[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
[CustomMessages]
de.StartupTask=Overlay beim Windows-Start starten
de.DeleteData=Auch Ihre Einstellungen, Bilder, Sounds und Protokolle löschen (der Ordner D4WorldBossOverlay in AppData)?
#endif

#if FileExists(CompilerPath + "Languages\Polish.isl")
[Languages]
Name: "pl"; MessagesFile: "compiler:Languages\Polish.isl"
[CustomMessages]
pl.StartupTask=Uruchamiaj nakładkę przy starcie Windows
pl.DeleteData=Usunąć także ustawienia, obrazy, dźwięki i dzienniki (folder D4WorldBossOverlay w AppData)?
#endif

#if FileExists(CompilerPath + "Languages\Turkish.isl")
[Languages]
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"
[CustomMessages]
tr.StartupTask=Windows başlarken overlay'i başlat
tr.DeleteData=Ayarlarınız, görselleriniz, sesleriniz ve günlükleriniz de silinsin mi (AppData içindeki D4WorldBossOverlay klasörü)?
#endif

#if FileExists(CompilerPath + "Languages\Japanese.isl")
[Languages]
Name: "ja"; MessagesFile: "compiler:Languages\Japanese.isl"
[CustomMessages]
ja.StartupTask=Windows の起動時にオーバーレイを起動する
ja.DeleteData=設定、画像、サウンド、ログ(AppData 内の D4WorldBossOverlay フォルダー)も削除しますか?
#endif

#if FileExists(CompilerPath + "Languages\Russian.isl")
[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
[CustomMessages]
ru.StartupTask=Запускать оверлей при старте Windows
ru.DeleteData=Удалить также ваши настройки, изображения, звуки и журналы (папка D4WorldBossOverlay в AppData)?
#endif

#if FileExists(CompilerPath + "Languages\Korean.isl")
[Languages]
Name: "ko"; MessagesFile: "compiler:Languages\Korean.isl"
[CustomMessages]
ko.StartupTask=Windows 시작 시 오버레이 실행
ko.DeleteData=설정, 이미지, 소리, 로그(AppData의 D4WorldBossOverlay 폴더)도 삭제하시겠습니까?
#endif

#if FileExists(CompilerPath + "Languages\ChineseSimplified.isl")
[Languages]
Name: "zh"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
[CustomMessages]
zh.StartupTask=Windows 启动时运行叠加层
zh.DeleteData=是否同时删除您的设置、图像、声音和日志(AppData 中的 D4WorldBossOverlay 文件夹)?
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "{cm:StartupTask}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#OverlayExe}"
Name: "{group}\{#AppName} - Settings"; Filename: "{app}\{#ConfigExe}"
Name: "{group}\{#AppName} - Licenses"; Filename: "{app}\LICENSES.txt"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#OverlayExe}"; Tasks: desktopicon

[Registry]
; Demarrage avec Windows (case de l'assistant). Le reglage correspondant est aussi ecrit dans settings.json (voir [Code]) :
; sans lui, l'overlay supprimerait cette entree a son premier lancement.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "D4Overlay"; ValueData: """{app}\{#OverlayExe}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#OverlayExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser
Filename: "{app}\{#ConfigExe}"; Description: "{cm:LaunchProgram,{#AppName} - Settings}"; Flags: nowait postinstall skipifsilent unchecked runasoriginaluser

[Code]
function SettingsPath: String;
begin
  Result := ExpandConstant('{userappdata}\D4WorldBossOverlay\settings.json');
end;

// Premiere installation : l'application demarre dans la langue choisie pour l'assistant (les codes de [Languages] sont ceux de
// l'application : en, fr, it, pt, es, de, pl, tr, ja, ru, ko, zh). Une installation existante n'est JAMAIS ecrasee.
procedure CurStepChanged(CurStep: TSetupStep);
var
  Path, Content: String;
  Raw: AnsiString;
  Startup: Boolean;
begin
  if CurStep <> ssPostInstall then Exit;

  Path := SettingsPath;
  Startup := WizardIsTaskSelected('startup');

  if not FileExists(Path) then
  begin
    ForceDirectories(ExtractFilePath(Path));
    Content := '{' + #13#10 + '  "Language": "' + ActiveLanguage + '"';
    if Startup then
      Content := Content + ',' + #13#10 + '  "StartWithWindows": true';
    Content := Content + #13#10 + '}';
    SaveStringToFile(Path, Content, False);
  end
  else if Startup then
  begin
    // Reinstallation avec la case cochee : on aligne le reglage de l'appli, sinon elle retirerait l'entree de demarrage.
    if LoadStringFromFile(Path, Raw) then
    begin
      Content := String(Raw);
      StringChangeEx(Content, '"StartWithWindows": false', '"StartWithWindows": true', True);
      SaveStringToFile(Path, Content, False);
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usPostUninstall then Exit;

  // L'entree de demarrage peut avoir ete creee par l'appli elle-meme (case "Lancer au demarrage de Windows") : on la retire toujours.
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'D4Overlay');

  // Les reglages sont conserves par defaut (reponse "Non") ; une desinstallation silencieuse ne pose pas la question.
  if not UninstallSilent then
    if MsgBox(CustomMessage('DeleteData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\D4WorldBossOverlay'), True, True, True);
end;
