#define MyAppName "NON PRO"
#define MyAppVersion "7.0.1"
#define MyAppPublisher "Pyshinka"
#define MyAppURL "https://t.me/pyshinka_channel"
#define MyAppExeName "Non Pro.exe"

[Setup]
AppId={{7A2D9E1F-3C5B-4D8E-A1F6-9B0C7D2E3F41}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
DisableDirPage=no
PrivilegesRequired=admin
OutputDir=C:\Users\user\Documents\FIRE SETUPS
OutputBaseFilename=NON_PRO_setup
SolidCompression=yes
WizardStyle=classic dark
SetupIconFile=C:\Users\user\.gemini\antigravity\scratch\file-transfer\client-wpf-dildman\app.ico

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "C:\temp\ft-build-dildman-8\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\app.ico"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runascurrentuser
