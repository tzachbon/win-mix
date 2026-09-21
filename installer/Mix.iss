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
VersionInfoVersion={#AppVersion}.0
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
Filename: "{app}\win-mix.exe"; Description: "Open Win Mix"; Flags: nowait postinstall skipifsilent; Check: not IsUpdate
Filename: "{app}\win-mix.exe"; Parameters: "--updated"; Flags: nowait; Check: IsUpdate

[UninstallDelete]
Type: files; Name: "{localappdata}\Mix.Native\settings.json"
Type: files; Name: "{localappdata}\Mix.Native\settings.json.tmp"
Type: files; Name: "{localappdata}\Mix.Native\last-error.txt"
Type: dirifempty; Name: "{localappdata}\Mix.Native"

[Code]
var
  FirstInstall: Boolean;

function IsUpdate: Boolean;
begin
  Result := ExpandConstant('{param:WINMIXUPDATE|0}') = '1';
end;

function NormalDirectory(const Path: String): Boolean;
var
  Entry: TFindRec;
begin
  Result := False;
  if FindFirst(Path, Entry) then
  begin
    Result := ((Entry.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
      ((Entry.Attributes and FILE_ATTRIBUTE_REPARSE_POINT) = 0);
    FindClose(Entry);
  end;
end;

function OwnedUpdateFile(const Name: String): Boolean;
var
  I: Integer;
  Suffix: String;
begin
  Result := False;
  if Length(Name) < 33 then exit;
  for I := 1 to 32 do
    if Pos(Copy(Name, I, 1), '0123456789abcdef') = 0 then exit;
  Suffix := Copy(Name, 33, Length(Name));
  Result := (Suffix = '.win-mix-update.exe') or (Suffix = '.win-mix-update.partial');
end;

procedure CleanUpdateCache;
var
  Parent, Cache: String;
  Entry: TFindRec;
begin
  Parent := ExpandConstant('{localappdata}\Mix.Native');
  Cache := Parent + '\Updates';
  if not NormalDirectory(Parent) or not NormalDirectory(Cache) then exit;
  if FindFirst(Cache + '\*', Entry) then
  begin
    try
      repeat
        if ((Entry.Attributes and (FILE_ATTRIBUTE_DIRECTORY or FILE_ATTRIBUTE_REPARSE_POINT)) = 0) and
          OwnedUpdateFile(Entry.Name) then DeleteFile(Cache + '\' + Entry.Name);
      until not FindNext(Entry);
    finally
      FindClose(Entry);
    end;
  end;
  RemoveDir(Cache);
end;

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
    CleanUpdateCache;
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Mix.Native');
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', 'Mix.Native');
  end;
end;


