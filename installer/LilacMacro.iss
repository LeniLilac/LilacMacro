#ifndef SourceRoot
  #error SourceRoot must be supplied by Build-Installer.ps1.
#endif
#ifndef PublishRoot
  #error PublishRoot must be supplied by Build-Installer.ps1.
#endif
#ifndef OutputRoot
  #error OutputRoot must be supplied by Build-Installer.ps1.
#endif
#ifndef AppVersion
  #error AppVersion must be supplied by Build-Installer.ps1.
#endif

[Setup]
AppId={{87C44822-4F2C-45E2-93DA-84098D39D1BC}
AppName=LilacMacro
AppVersion={#AppVersion}
AppPublisher=LilacMacro contributors
AppPublisherURL=https://github.com/LeniLilac/LilacMacro
DefaultDirName={autopf}\LilacMacro
DefaultGroupName=LilacMacro
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
OutputDir={#OutputRoot}
OutputBaseFilename=LilacMacro-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\LilacMacro.exe
LicenseFile={#SourceRoot}\LICENSE.md
SetupLogging=yes
CloseApplications=no
RestartApplications=no

[Files]
Source: "{#PublishRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\third_party\termwrap\v0.6\*"; DestDir: "{app}\native\termwrap\v0.6"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\LICENSE.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\NOTICE.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\LilacMacro"; Filename: "{app}\LilacMacro.exe"
Name: "{autodesktop}\LilacMacro"; Filename: "{app}\LilacMacro.exe"; Tasks: desktopicon

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Run]
Filename: "{app}\LilacMacro.exe"; Description: "Launch LilacMacro"; Flags: nowait postinstall skipifsilent; Check: not IsCoordinatedUpdate
Filename: "{app}\LilacMacro.exe"; Flags: nowait; Check: IsCoordinatedUpdate

[Code]
const
  SynchronizeAccess = $00100000;
  WaitTimeout = 258;
  ErrorInvalidParameter = 87;

type
  TSystemTime = record
    wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds: Word;
  end;

var
  UpdateStatePath: String;
  UpdateRequestPath: String;
  UpdateOperationId: String;
  UpdateTargetVersion: String;
  UpdateInstallerSha256: String;
  UpdateStateLoaded: Boolean;
  ParticipantPids: array of Integer;

function OpenProcess(DesiredAccess: LongWord; InheritHandle: Boolean; ProcessId: LongWord): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function GetLastError: LongWord;
  external 'GetLastError@kernel32.dll stdcall';
procedure GetSystemTime(var SystemTime: TSystemTime);
  external 'GetSystemTime@kernel32.dll stdcall';

function IsCoordinatedUpdate: Boolean;
begin
  Result := UpdateStateLoaded;
end;

function StateValue(const Lines: TArrayOfString; const Prefix: String; var Value: String): Boolean;
var
  I, Count: Integer;
begin
  Count := 0;
  Value := '';
  for I := 0 to GetArrayLength(Lines) - 1 do
    if Pos(Prefix, Lines[I]) = 1 then begin
      Count := Count + 1;
      Value := Copy(Lines[I], Length(Prefix) + 1, MaxInt);
    end;
  Result := (Count = 1) and (Value <> '');
end;

function IsHexDigest(const Value: String): Boolean;
var
  I: Integer;
begin
  Result := Length(Value) = 64;
  for I := 1 to Length(Value) do
    if Pos(Value[I], '0123456789abcdefABCDEF') = 0 then Result := False;
end;

function LoadCoordinatedUpdateState: Boolean;
var
  Lines: TArrayOfString;
  Root, Schema, Value: String;
  I, Pid, Count: Integer;
begin
  Result := False;
  UpdateStatePath := ExpandConstant('{param:UPDATESTATE|}');
  if UpdateStatePath = '' then begin
    Result := True;
    exit;
  end;
  UpdateStatePath := ExpandFileName(UpdateStatePath);
  Root := AddBackslash(ExpandConstant('{localappdata}\LilacMacro\updates'));
  if (Pos(Lowercase(Root), Lowercase(UpdateStatePath)) <> 1)
    or (CompareText(ExtractFileName(UpdateStatePath), 'update-state.txt') <> 0)
    or not LoadStringsFromFile(UpdateStatePath, Lines) then exit;
  if not StateValue(Lines, 'schema_version=', Schema) or (Schema <> '1') then exit;
  if not StateValue(Lines, 'operation_id=', UpdateOperationId) then exit;
  if not StateValue(Lines, 'target_version=', UpdateTargetVersion) then exit;
  if not StateValue(Lines, 'installer_sha256=', UpdateInstallerSha256) or not IsHexDigest(UpdateInstallerSha256) then exit;
  if not StateValue(Lines, 'request_path=', UpdateRequestPath) then exit;
  if CompareText(ExpandFileName(UpdateRequestPath), ExpandConstant('{commonappdata}\LilacMacro\Session\update-request.txt')) <> 0 then exit;
  if UpdateTargetVersion <> '{#AppVersion}' then exit;
  if CompareText(GetSHA256OfFile(ExpandConstant('{srcexe}')), UpdateInstallerSha256) <> 0 then exit;

  Count := 0;
  for I := 0 to GetArrayLength(Lines) - 1 do
    if Pos('participant_pid=', Lines[I]) = 1 then begin
      Value := Copy(Lines[I], Length('participant_pid=') + 1, MaxInt);
      Pid := StrToIntDef(Value, 0);
      if (Pid <= 0) or (Count >= 64) then exit;
      SetArrayLength(ParticipantPids, Count + 1);
      ParticipantPids[Count] := Pid;
      Count := Count + 1;
    end;
  if Count = 0 then exit;
  UpdateStateLoaded := True;
  Result := True;
end;

function InitializeSetup: Boolean;
begin
  Result := LoadCoordinatedUpdateState;
  if not Result then
    MsgBox('The coordinated update state is invalid or no longer matches this installer.', mbError, MB_OK);
end;

function UtcTimestamp: String;
var
  Value: TSystemTime;
begin
  GetSystemTime(Value);
  Result := Format('%.4d-%.2d-%.2dT%.2d:%.2d:%.2d.0000000+00:00', [Value.wYear,
    Value.wMonth, Value.wDay, Value.wHour, Value.wMinute, Value.wSecond]);
end;

function ProcessStillRunning(ProcessId: Integer; var InspectionFailed: Boolean): Boolean;
var
  Handle: THandle;
  ErrorCode, WaitResult: LongWord;
begin
  InspectionFailed := False;
  Handle := OpenProcess(SynchronizeAccess, False, ProcessId);
  if Handle = 0 then begin
    ErrorCode := GetLastError;
    Result := ErrorCode <> ErrorInvalidParameter;
    InspectionFailed := Result;
    exit;
  end;
  WaitResult := WaitForSingleObject(Handle, 0);
  CloseHandle(Handle);
  Result := WaitResult = WaitTimeout;
  InspectionFailed := (WaitResult <> 0) and (WaitResult <> WaitTimeout);
end;

function WaitForUpdateParticipants: String;
var
  Attempt, I: Integer;
  Running, InspectionFailed, Failed: Boolean;
begin
  Result := '';
  for Attempt := 1 to 360 do begin
    Running := False;
    Failed := False;
    for I := 0 to GetArrayLength(ParticipantPids) - 1 do begin
      if ProcessStillRunning(ParticipantPids[I], InspectionFailed) then Running := True;
      if InspectionFailed then Failed := True;
    end;
    if Failed then begin
      Result := 'A running LilacMacro process could not be inspected. The update was not installed.';
      exit;
    end;
    if not Running then exit;
    Sleep(250);
  end;
  Result := 'LilacMacro did not close every active session within 90 seconds. The update was not installed.';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  RequestText: String;
begin
  Result := '';
  if not IsCoordinatedUpdate then exit;
  if not ForceDirectories(ExtractFileDir(UpdateRequestPath)) then begin
    Result := 'The coordinated update request directory could not be created.';
    exit;
  end;
  RequestText := 'schema_version=1' + #13#10
    + 'operation_id=' + UpdateOperationId + #13#10
    + 'target_version=' + UpdateTargetVersion + #13#10
    + 'requested_utc=' + UtcTimestamp + #13#10;
  if not SaveStringToFile(UpdateRequestPath, RequestText, False) then begin
    Result := 'The coordinated update shutdown request could not be written.';
    exit;
  end;
  Result := WaitForUpdateParticipants;
  if Result <> '' then DeleteFile(UpdateRequestPath);
end;

function RunnerJournalExists: Boolean;
begin
  Result := FileExists(ExpandConstant('{commonappdata}\LilacMacro\Session\provisioning.json'));
end;

procedure RelaunchUpdateParticipants;
var
  ResultCode: Integer;
begin
  if not IsCoordinatedUpdate then exit;
  if not Exec(ExpandConstant('{app}\LilacMacro.SessionSetup.exe'),
    'relaunch-update "' + UpdateStatePath + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
    or (ResultCode <> 0) then
    Log('The upgraded local runner UIs could not all be relaunched. Reopen their sessions manually.');
  DeleteFile(UpdateRequestPath);
end;

procedure AttemptRunnerRepair;
var
  ResultCode: Integer;
begin
  if not RunnerJournalExists then
    exit;
  if not Exec(ExpandConstant('{app}\LilacMacro.SessionSetup.exe'), 'repair', '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    Log('Optional local runner migration did not complete. The application upgrade will continue with the runner unavailable until Repair succeeds.');
end;

procedure RequireRunnerCleanup;
var
  ResultCode: Integer;
begin
  if not RunnerJournalExists then
    exit;
  if not Exec(ExpandConstant('{app}\LilacMacro.SessionSetup.exe'), 'uninstall-cleanup', '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    RaiseException('Local runner cleanup is incomplete. LilacMacro was retained. Run REMOVE or retry uninstall.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    AttemptRunnerRepair;
    RelaunchUpdateParticipants;
  end;
end;

procedure DeinitializeSetup;
begin
  if IsCoordinatedUpdate then DeleteFile(UpdateRequestPath);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RequireRunnerCleanup;
end;
