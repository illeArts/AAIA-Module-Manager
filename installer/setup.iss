; ============================================================
;  AAIA Module Manager — Inno Setup Script
;  Erstellt einen Windows-Installer mit automatischem Deinstaller.
;
;  Voraussetzung: build-installer.bat zuerst ausführen,
;  damit publish\win-x64\ gefüllt ist.
; ============================================================

#define AppName      "AAIA Module Manager"
#define AppVersion   "2.3.0"
#define AppPublisher "André Iljaschow / IleArts"
#define AppURL       "https://aaia.dev"
#define AppExeName   "AAIA.ModuleManager.exe"
#define AppGUID      "{{A1B2C3D4-E5F6-4A7B-8C9D-E0F1A2B3C4D5}"

; Pfad zur publizierten App (relativ zu dieser .iss Datei)
#define PublishDir   "..\publish\win-x64"

[Setup]
AppId={#AppGUID}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} v{#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
; Kein Admin-Recht nötig falls User-Install bevorzugt wird:
; PrivilegesRequired=lowest
; PrivilegesRequiredOverridesAllowed=dialog
DisableProgramGroupPage=yes
OutputDir=dist
OutputBaseFilename=AAIA_ModuleManager_v{#AppVersion}_Setup
; Icon: wird automatisch verwendet wenn vorhanden (create-icon.ps1 zuerst ausführen)
#define IconFile "..\src\AAIA.ModuleManager\Assets\AAIA_Module_Manager.ico"
#if FileExists(IconFile)
SetupIconFile={#IconFile}
#endif
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=120
; Minimale Windows-Version: Windows 10 (1809+)
MinVersion=10.0.17763
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Installer

[Languages]
Name: "german";  MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Alle publizierten Dateien (selbst-enthaltene Exe + ggf. native Libs)
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Startmenü
Name: "{group}\{#AppName}";                Filename: "{app}\{#AppExeName}"
Name: "{group}\{#AppName} deinstallieren"; Filename: "{uninstallexe}"
; Desktop (nur wenn Task ausgewählt)
Name: "{autodesktop}\{#AppName}";          Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Nach Installation: App direkt starten (optional, Checkbox)
Filename: "{app}\{#AppExeName}"; \
  Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Config-Verzeichnis beim Deinstallieren aufräumen (mit Nutzer-Bestätigung über Checkbox)
; Hinweis: config.json in %AppData%\AAIAModuleManager bleibt bewusst erhalten,
; damit bei Neuinstallation Einstellungen + ETW-Token nicht verloren gehen.
; Wer alles löschen will, kann den Ordner manuell entfernen.
Type: filesandordirs; Name: "{app}"

[Code]
// ── WinAPI ────────────────────────────────────────────────────────────────────

function SetForegroundWindow(hWnd: HWND): BOOL;
  external 'SetForegroundWindow@user32.dll stdcall';
function ShowWindow(hWnd: HWND; nCmdShow: Integer): BOOL;
  external 'ShowWindow@user32.dll stdcall';

// ── Wizard-Initialisierung ────────────────────────────────────────────────────

procedure InitializeWizard();
begin
  // Installer-Fenster in den Vordergrund erzwingen
  ShowWindow(WizardForm.Handle, 9);       // SW_RESTORE = 9
  SetForegroundWindow(WizardForm.Handle);
  WizardForm.BringToFront;

  // Willkommensseite: Untertitel anpassen
  WizardForm.WelcomeLabel2.Caption :=
    'Dieses Programm installiert ' + '{#AppName}' + ' v{#AppVersion}' +
    ' auf deinem Computer.' + #13#10 + #13#10 +
    'Das Tool ermöglicht ETW-Entwicklern das Verwalten, Testen und' +
    ' Veröffentlichen von AAIA-Modulen und Plugins.';
end;
