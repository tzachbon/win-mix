#ifndef AppVersion
  #error AppVersion must be supplied by build.ps1
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif
[Setup]
AppId={{724DA822-9BEC-4E81-8487-2C267218221A}
AppName=Win Mix
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher=Win Mix
DefaultDirName={localappdata}\Programs\Mix.Native
DefaultGroupName=Win Mix
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\..\
OutputBaseFilename=win-mix-Setup-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\win-mix.exe
CloseApplications=no
RestartApplications=no
SetupLogging=yes

[Tasks]
Name: startup; Description: "Start Win Mix with Windows"; Check: IsFirstInstall

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Win Mix"; Filename: "{app}\win-mix.exe"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Mix.Native"; ValueData: """{app}\win-mix.exe"" --background"; Tasks: startup; Check: IsFirstInstall; Flags: uninsdeletevalue

[Run]
Filename: "{app}\win-mix.exe"; Description: "Open Win Mix"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{localappdata}\Mix.Native\settings.json"
Type: files; Name: "{localappdata}\Mix.Native\settings.json.tmp"
Type: files; Name: "{localappdata}\Mix.Native\last-error.txt"
Type: dirifempty; Name: "{localappdata}\Mix.Native"

[Code]
var
  FirstInstall: Boolean;

function IsFirstInstall: Boolean;
begin
  Result := FirstInstall;
end;

function InitializeSetup: Boolean;
begin
  FirstInstall := not RegKeyExists(HKCU,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\{724DA822-9BEC-4E81-8487-2C267218221A}_is1');
  Result := True;
end;

function StopMix: Boolean;
var
  ExitCode: Integer;
  Success: Boolean;
begin
  Result := True;
  if not FileExists(ExpandConstant('{app}\win-mix.exe')) then exit;
  repeat
    Success := Exec(ExpandConstant('{app}\win-mix.exe'), '--shutdown',
      ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ExitCode);
    if Success and (ExitCode = 0) then exit;
    Result := False;
    if SuppressibleMsgBox('Win Mix is still running. Exit Win Mix from its tray menu, then choose Retry.',
      mbError, MB_RETRYCANCEL, IDCANCEL) <> IDRETRY then exit;
    Result := True;
  until False;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not StopMix then Result := 'Win Mix must exit before installation can continue.';
end;

function InitializeUninstall: Boolean;
begin
  Result := StopMix;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Mix.Native');
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', 'Mix.Native');
  end;
end;


