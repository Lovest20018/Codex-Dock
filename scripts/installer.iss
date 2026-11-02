#ifndef AppVersion
  #define AppVersion "0.16.1"
#endif
#ifndef PayloadDir
  #define PayloadDir "..\artifacts\CodexDock-0.16.1-win-x64"
#endif

[Setup]
AppId={{D20748E0-BC17-4CB7-AD07-CE57F065D5F1}
AppName=Codex Dock
AppVersion={#AppVersion}
AppVerName=Codex Dock {#AppVersion}
AppPublisher=Codex Dock
DefaultDirName={localappdata}\Programs\Codex Config
DefaultGroupName=Codex Dock
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts
OutputBaseFilename=CodexDock-{#AppVersion}-Setup-win-x64
SetupIconFile=..\src\CodexConfig.App\Assets\app.ico
UninstallDisplayIcon={app}\CodexDock.exe
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
CreateUninstallRegKey=ShouldRegister
DisableWelcomePage=no
UsePreviousAppDir=yes
VersionInfoVersion={#AppVersion}.0

[InstallDelete]
Type: files; Name: "{app}\CodexConfig.exe"
Type: files; Name: "{app}\CodexConfig.dll"
Type: files; Name: "{app}\CodexConfig.deps.json"
Type: files; Name: "{app}\CodexConfig.runtimeconfig.json"
Type: files; Name: "{app}\CodexConfig.pdb"
Type: files; Name: "{group}\Codex Config.lnk"; Check: ShouldRegister
Type: files; Name: "{autodesktop}\Codex Config.lnk"; Check: ShouldRegister
Type: files; Name: "{autodesktop}\Codex Dock.lnk"; Check: RemoveDesktopShortcut

[Languages]
Name: "chinesesimp"; MessagesFile: "..\third_party\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked; Check: ShouldRegister

[Files]
Source: "installer-stop-mobile.ps1"; Flags: dontcopy
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Codex Dock"; Filename: "{app}\CodexDock.exe"; Check: ShouldRegister
Name: "{autodesktop}\Codex Dock"; Filename: "{app}\CodexDock.exe"; Tasks: desktopicon; Check: ShouldRegister

[Run]
Filename: "{app}\CodexDock.exe"; Description: "打开 Codex Dock"; Flags: nowait postinstall skipifsilent; Check: ShouldRegister

[Code]
function IsVerificationInstall: Boolean;
begin
  Result := ExpandConstant('{param:VERIFYONLY|0}') = '1';
end;

function ShouldRegister: Boolean;
begin
  Result := not IsVerificationInstall;
end;

function RemoveDesktopShortcut: Boolean;
begin
  Result := ShouldRegister and (not WizardIsTaskSelected('desktopicon'));
end;

function InitializeSetup: Boolean;
begin
  { Verification installs may only target an explicit directory. }
  Result := (not IsVerificationInstall) or (ExpandConstant('{param:DIR|}') <> '');
end;

procedure RegisterExtraCloseApplicationsResources;
begin
  RegisterExtraCloseApplicationsResource(False, ExpandConstant('{app}\CodexConfig.exe'));
  RegisterExtraCloseApplicationsResource(False, ExpandConstant('{app}\mobile\CodexDock.Mobile.exe'));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var ExitCode: Integer; Arguments: String;
begin
  Result := '';
  ExtractTemporaryFile('installer-stop-mobile.ps1');
  Arguments := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\installer-stop-mobile.ps1') +
    '" -InstallDir "' + ExpandConstant('{app}') + '"';
  if IsVerificationInstall and (ExpandConstant('{param:MOBILESTATEROOT|}') <> '') then
    Arguments := Arguments + ' -StateRoot "' + ExpandConstant('{param:MOBILESTATEROOT|}') + '"';
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    Arguments, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    ExitCode := -1;
  if ExitCode <> 0 then
    Result := '手机网关尚未完全退出。请在旧版 Dock 的“手机连接”页点击“停止”，退出 Dock 后重试安装。';
end;

function StartupTargetsThisInstall(const Command: String): Boolean;
begin
  Result := (CompareText(Copy(Command, 1, Length('"' + ExpandConstant('{app}\CodexDock.exe') + '" ')), '"' + ExpandConstant('{app}\CodexDock.exe') + '" ') = 0);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Command: String;
begin
  if (CurUninstallStep = usPostUninstall) and (not IsVerificationInstall) then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CodexDock', Command) then
      if StartupTargetsThisInstall(Command) then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CodexDock');
end;
