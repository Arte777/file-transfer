#define MyAppName "RAH NonPro"
#define MyAppVersion "7"
#define MyAppPublisher "Убежище"
#define MyAppURL "https://t.me/robloxvzlomez"
#define MyAppExeName "RAH Non Pro.exe"

[Setup]
AppId={{58C6864B-68B4-4031-BD27-7AF71E9E8037}
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
PrivilegesRequired=admin
OutputDir=C:\Users\user\Documents\RAH SETUPS
OutputBaseFilename=RAH_Non_Pro_setup
SolidCompression=yes
WizardStyle=classic dark
SetupIconFile=C:\Users\user\.gemini\antigravity\scratch\file-transfer\client-wpf\app.ico

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "C:\Users\user\.gemini\antigravity\scratch\file-transfer\client-wpf\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\app.ico"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runascurrentuser
