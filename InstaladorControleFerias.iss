#define MyAppName "Controle de Férias"
#define MyAppVersion "1.0.0"
#define MyAppExeName "ControleFerias.exe"
#define ProjectRoot AddBackslash(SourcePath)
// Sempre publica o codigo atual, inclusive ao compilar pela interface do Inno Setup.
// Uma falha interrompe a compilacao para nao reutilizar arquivos antigos.
#if Exec("dotnet.exe", "publish " + AddQuotes(ProjectRoot + "FÉRIAS_CAMPOS.csproj") + " -c Release -r win-x64 --self-contained true -p:UseAppHost=true", ProjectRoot, 1, SW_HIDE) != 0
  #error Falha ao publicar a versao Release. Verifique o SDK .NET e execute dotnet publish para consultar o erro.
#endif
#define PublishRoot ProjectRoot + "bin\Release\publish\"
[Setup]
AppId={{D67D8F7F-101E-4AF1-9FCB-C9EF2B1EAFE1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\Campos\Controle de Férias
DefaultGroupName={#MyAppName}
OutputDir=installer
OutputBaseFilename=ControleFerias-Setup-{#GetDateTimeString('yyyymmdd-hhnnss')}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
Compression=lzma2
SolidCompression=yes
SetupIconFile={#ProjectRoot}Resource\program.ico
UninstallDisplayIcon={app}\program.ico
CloseApplications=yes
[Files]
Source: "{#PublishRoot}*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\program.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\program.ico"; Tasks: desktopicon
[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"
[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir {#MyAppName}"; Flags: nowait postinstall skipifsilent
