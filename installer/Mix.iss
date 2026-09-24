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
  StopMixError: String;

type
  TMixProcessState = (msUnknown, msStopped, msRunning);

function CompareStringOrdinal(const Left: String; LeftLength: Integer;
  const Right: String; RightLength, IgnoreCase: Integer): Integer;
  external 'CompareStringOrdinal@kernel32.dll stdcall';

function GetLongPathName(const ShortPath, LongPath: String; BufferLength: Cardinal): Cardinal;
  external 'GetLongPathNameW@kernel32.dll stdcall';

function NormalizeMixPath(const Path: String): String;
var
  Buffer: String;
  Count: Cardinal;
begin
  Result := Path;
  if Copy(Result, 1, 8) = '\\?\UNC\' then Result := '\\' + Copy(Result, 9, Length(Result))
  else if Copy(Result, 1, 4) = '\\?\' then Delete(Result, 1, 4);
  if Length(Result) < 3 then RaiseException('Missing executable path.');
  if not (((Result[2] = ':') and (Result[3] = '\')) or (Copy(Result, 1, 2) = '\\')) then
    RaiseException('Executable path is not absolute.');
  Result := ExpandFileName(Result);
  SetLength(Buffer, 32768);
  Count := GetLongPathName(Result, Buffer, Length(Buffer));
  if (Count > 0) and (Count < Cardinal(Length(Buffer))) then Result := Copy(Buffer, 1, Count);
end;

function MixProcessState: TMixProcessState;
var
  Locator, Services, Processes, Path: Variant;
  Target: String;
  I: Integer;
begin
  Result := msUnknown;
  try
    Target := NormalizeMixPath(ExpandConstant('{app}\win-mix.exe'));
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Services := Locator.ConnectServer('', 'root\CIMV2');
    Processes := Services.ExecQuery('SELECT ExecutablePath FROM Win32_Process WHERE Name = ''win-mix.exe''');
    for I := 0 to Processes.Count - 1 do
    begin
      Path := Processes.ItemIndex(I).ExecutablePath;
      if VarIsNull(Path) or VarIsEmpty(Path) then exit;
      if CompareStringOrdinal(NormalizeMixPath(Path), -1, Target, -1, 1) = 2 then
      begin
        Result := msRunning;
        exit;
      end;
    end;
    Result := msStopped;
  except
    Log('Could not query Win Mix process state: ' + GetExceptionMessage);
  end;
end;

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
  State: TMixProcessState;
begin
  Result := False;
  StopMixError := '';
  repeat
    State := MixProcessState;
    if State = msStopped then begin Result := True; exit; end;
    StopMixError := 'Could not verify whether Win Mix is running. No files were changed. Close Win Mix and retry; see the setup log if this continues.';
    if State = msRunning then
    begin
      Success := Exec(ExpandConstant('{app}\win-mix.exe'), '--shutdown',
        ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ExitCode);
      Log(Format('Win Mix shutdown: launched=%d, result=%d', [Ord(Success), ExitCode]));
      State := MixProcessState;
      if State = msStopped then begin Result := True; exit; end;
      if State = msRunning then
      begin
        StopMixError := 'Win Mix is still running. Exit Win Mix from its tray menu, then choose Retry.';
        if not Success or (ExitCode <> 0) then
          StopMixError := 'The Win Mix shutdown command failed and Win Mix is still running. Exit it from its tray menu, then choose Retry.';
      end;
    end;
    Log(StopMixError);
    if IsUninstaller then begin if UninstallSilent then exit; end
    else if WizardSilent then exit;
    if SuppressibleMsgBox(StopMixError,
      mbError, MB_RETRYCANCEL, IDCANCEL) <> IDRETRY then exit;
  until False;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not StopMix then Result := StopMixError;
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


