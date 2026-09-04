#define MyAppName "Controle de Férias"
#define MyAppVersion "1.0.0"
#define MyAppExeName "ControleFerias.exe"
[Setup]
AppId={{D67D8F7F-101E-4AF1-9FCB-C9EF2B1EAFE1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\Campos\Controle de Férias
DefaultGroupName={#MyAppName}
OutputDir=installer
OutputBaseFilename=ControleFerias-Setup
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
Compression=lzma2
SolidCompression=yes
[Files]
Source: "bin\Release\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"
[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir {#MyAppName}"; Flags: nowait postinstall skipifsilent
