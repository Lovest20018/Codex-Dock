#ifndef AppVersion
  #define AppVersion "0.8.0"
#endif
#ifndef PayloadDir
  #define PayloadDir "..\artifacts\CodexConfig-0.8.0-win-x64"
#endif

[Setup]
AppId={{D20748E0-BC17-4CB7-AD07-CE57F065D5F1}
AppName=Codex Config
AppVersion={#AppVersion}
AppVerName=Codex Config {#AppVersion}
AppPublisher=Codex Config
DefaultDirName={localappdata}\Programs\Codex Config
DefaultGroupName=Codex Config
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts
OutputBaseFilename=CodexConfig-{#AppVersion}-Setup-win-x64
SetupIconFile=..\src\CodexConfig.App\Assets\app.ico
UninstallDisplayIcon={app}\CodexConfig.exe
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
CreateUninstallRegKey=ShouldRegister
DisableWelcomePage=no
UsePreviousAppDir=yes
VersionInfoVersion={#AppVersion}.0

[Languages]
Name: "chinesesimp"; MessagesFile: "..\third_party\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked; Check: ShouldRegister

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Codex Config"; Filename: "{app}\CodexConfig.exe"; Check: ShouldRegister
Name: "{autodesktop}\Codex Config"; Filename: "{app}\CodexConfig.exe"; Tasks: desktopicon; Check: ShouldRegister

[Run]
Filename: "{app}\CodexConfig.exe"; Description: "打开 Codex Config"; Flags: nowait postinstall skipifsilent; Check: ShouldRegister

[Code]
function IsVerificationInstall: Boolean;
begin
  Result := ExpandConstant('{param:VERIFYONLY|0}') = '1';
end;

function ShouldRegister: Boolean;
begin
  Result := not IsVerificationInstall;
end;

function InitializeSetup: Boolean;
begin
  { Verification installs may only target an explicit directory. }
  Result := (not IsVerificationInstall) or (ExpandConstant('{param:DIR|}') <> '');
end;
