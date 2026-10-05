; Build with scripts/Build-Installer.ps1. Keep AppId stable across upgrades.
#ifndef AppVersion
  #define AppVersion "1.5.1"
#endif
#define AppName "Launchpad"
#define PublishDir SourcePath + "..\artifacts\publish\win-x64"

[Setup]
AppId={{E69C9705-E0BD-4ED3-BA0A-E1DA82C85466}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\GameLauncher
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
OutputDir=..\artifacts\installer
OutputBaseFilename=GameLauncher-Setup-{#AppVersion}-win-x64
SetupIconFile=..\src\GameLauncher\GameLauncher.ico
UninstallDisplayIcon={app}\GameLauncher.exe
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
AppMutex=Local\GameLauncher.Desktop
SetupMutex=GameLauncher.Setup
CloseApplications=no
RestartApplications=no
InfoBeforeFile=BeforeInstall.txt
Uninstallable=yes

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml,web.config"
Source: "..\artifacts\prerequisites\MicrosoftEdgeWebview2Setup.exe"; Flags: dontcopy

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\GameLauncher.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\GameLauncher.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\GameLauncher.exe"; Description: "Open {#AppName}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
const
  WebViewKey = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

function HasRuntimeAt(RootKey: Integer): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(RootKey, WebViewKey, 'pv', Version) and
    (Trim(Version) <> '') and (Version <> '0.0.0.0');
end;

function HasWebViewRuntime: Boolean;
begin
  Result := HasRuntimeAt(HKLM32) or HasRuntimeAt(HKCU32) or HasRuntimeAt(HKCU64);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  if HasWebViewRuntime then Exit;
  WizardForm.StatusLabel.Caption := 'Installing Microsoft Edge WebView2 Runtime (internet required)...';
  ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
  if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'), '/silent /install', '',
    SW_HIDE, ewWaitUntilTerminated, ExitCode) then
  begin
    Result := 'Could not start the Microsoft WebView2 installer: ' + SysErrorMessage(ExitCode);
    Exit;
  end;
  Log('WebView2 installer exit code: ' + IntToStr(ExitCode));
  if not HasWebViewRuntime then
    Result := 'Microsoft Edge WebView2 Runtime is still missing (installer code ' + IntToStr(ExitCode) + '). ' +
      'Check your internet connection and retry, or install WebView2 from ' +
      'https://developer.microsoft.com/microsoft-edge/webview2/consumer/ before running Setup again.';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    { Remove only startup registration pointing to THIS installed copy. }
    if RegQueryStringValue(HKCU64, RunKey, 'GameLauncher', Command) and
      (CompareText(Command, '"' + ExpandConstant('{app}\GameLauncher.exe') + '"') = 0) then
      RegDeleteValue(HKCU64, RunKey, 'GameLauncher');
    { Library, artwork, credentials, WebView2 data and preferences are deliberately preserved.
      Never recursively delete the installation directory or touch any user-selected game/browser paths. }
  end;
end;
