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
InfoBeforeFile={#SourceRoot}\TERMS.md
SetupLogging=yes
CloseApplications=no
RestartApplications=no

[Files]
Source: "{#PublishRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\third_party\termwrap\v0.6\*"; DestDir: "{app}\native\termwrap\v0.6"; Flags: ignoreversion recursesubdirs createallsubdirs onlyifdoesntexist
Source: "{#SourceRoot}\LICENSE.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\NOTICE.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\PRIVACY.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\TERMS.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\licenses\OCR-RUNTIME.md"; DestDir: "{app}\licenses"; Flags: ignoreversion

[InstallDelete]
Type: filesandordirs; Name: "{app}\Assets\RuntimeEvidence"

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
  InvalidFileAttributes = $FFFFFFFF;
  FileAttributeDirectory = $00000010;
  FileAttributeReparsePoint = $00000400;

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
  RunnerRepairSucceeded: Boolean;
procedure GetSystemTime(var SystemTime: TSystemTime);
  external 'GetSystemTime@kernel32.dll stdcall';
function GetFileAttributes(FileName: String): LongWord;
  external 'GetFileAttributesW@kernel32.dll stdcall';

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
  if CompareText(ExpandFileName(UpdateRequestPath), ExpandConstant('{commonappdata}\LilacMacro\UpdateControl\update-request.txt')) <> 0 then exit;
  if UpdateTargetVersion <> '{#AppVersion}' then exit;
  if CompareText(GetSHA256OfFile(ExpandConstant('{srcexe}')), UpdateInstallerSha256) <> 0 then exit;

  Count := 0;
  for I := 0 to GetArrayLength(Lines) - 1 do
    if Pos('participant_pid=', Lines[I]) = 1 then begin
      Value := Copy(Lines[I], Length('participant_pid=') + 1, MaxInt);
      Pid := StrToIntDef(Value, 0);
      if (Pid <= 0) or (Count >= 64) then exit;
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

function ManualUpdateOperationId: String;
begin
  { Manual requests are identified by their fresh timestamp and target version;
    the operation id only satisfies the shared request schema. }
  Result := '87c44822-4f2c-45e2-93da-84098d39d1bc';
end;

function ForceCloseProductImage(const ImageName: String): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if not Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM "' + ImageName + '"', '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode) then begin
    Result := 'Windows could not start the bounded LilacMacro shutdown fallback.';
    exit;
  end;
  { taskkill returns 128 when no matching process exists. }
  if (ResultCode <> 0) and (ResultCode <> 128) then
    Result := 'Windows could not stop ' + ImageName + ' before the upgrade (exit code '
      + IntToStr(ResultCode) + ').';
end;

function StopManualUpdateProcesses: String;
begin
  Log('Requesting bounded shutdown of any remaining LilacMacro UI processes.');
  Result := ForceCloseProductImage('LilacMacro.exe');
  if Result = '' then Result := ForceCloseProductImage('LilacMacro.RuntimeLab.exe');
  if Result = '' then Result := ForceCloseProductImage('LilacMacro.DatasetBuilder.exe');
  if Result = '' then Result := ForceCloseProductImage('LilacMacro.DeepDebugViewer.exe');
  if Result = '' then Sleep(1000);
end;

procedure RequestCloseProductImage(const ImageName: String; var Requested: Boolean);
var
  ResultCode: Integer;
begin
  ResultCode := 0;
  if not Exec(ExpandConstant('{sys}\taskkill.exe'), '/T /IM "' + ImageName + '"', '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode) then begin
    Log('Windows could not start the graceful shutdown request for ' + ImageName + '.');
    exit;
  end;
  if ResultCode = 0 then Requested := True;
  if (ResultCode <> 0) and (ResultCode <> 128) then
    Log('The graceful shutdown request for ' + ImageName + ' returned exit code '
      + IntToStr(ResultCode) + '; the bounded force-close fallback will run.');
end;

procedure StopUninstallProcesses;
var
  CleanupError: String;
  Requested: Boolean;
begin
  Log('Requesting bounded shutdown of LilacMacro UI processes before uninstall.');
  Requested := False;
  RequestCloseProductImage('LilacMacro.exe', Requested);
  RequestCloseProductImage('LilacMacro.RuntimeLab.exe', Requested);
  RequestCloseProductImage('LilacMacro.DatasetBuilder.exe', Requested);
  RequestCloseProductImage('LilacMacro.DeepDebugViewer.exe', Requested);
  if Requested then Sleep(5000);
  CleanupError := StopManualUpdateProcesses;
  if CleanupError <> '' then
    RaiseException('LilacMacro could not close every application window before uninstall. ' + CleanupError);
end;

function SafeDirectory(const Path: String): Boolean;
var
  Attributes: LongWord;
begin
  Attributes := GetFileAttributes(Path);
  Result := (Attributes <> InvalidFileAttributes)
    and ((Attributes and FileAttributeDirectory) <> 0)
    and ((Attributes and FileAttributeReparsePoint) = 0);
end;

function SecureUpdateControlRoot: String;
var
  ProductRoot, ControlRoot, Arguments: String;
  Attributes: LongWord;
  ResultCode: Integer;
begin
  Result := '';
  ProductRoot := ExpandConstant('{commonappdata}\LilacMacro');
  ControlRoot := ExtractFileDir(UpdateRequestPath);
  if not ForceDirectories(ControlRoot) then begin
    Result := 'The LilacMacro update request directory could not be created.';
    exit;
  end;
  if not SafeDirectory(ProductRoot) or not SafeDirectory(ControlRoot) then begin
    Result := 'The LilacMacro update request directory is not a safe local directory.';
    exit;
  end;
  Arguments := '"' + ControlRoot + '" /inheritance:r /grant:r '
    + '"*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" '
    + '"*S-1-5-32-545:(OI)(CI)RX"';
  if not Exec(ExpandConstant('{sys}\icacls.exe'), Arguments, '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then begin
    Result := 'The LilacMacro update request directory could not be secured.';
    exit;
  end;
  if not SafeDirectory(ProductRoot) or not SafeDirectory(ControlRoot) then begin
    Result := 'The LilacMacro update request directory changed while it was being secured.';
    exit;
  end;
  Attributes := GetFileAttributes(UpdateRequestPath);
  if (Attributes <> InvalidFileAttributes)
    and ((Attributes and FileAttributeReparsePoint) <> 0) then
    Result := 'The LilacMacro update request file is an unsafe reparse point.';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  RequestText: String;
begin
  Result := '';
  if not IsCoordinatedUpdate then begin
    UpdateRequestPath := ExpandConstant('{commonappdata}\LilacMacro\UpdateControl\update-request.txt');
    UpdateOperationId := ManualUpdateOperationId;
    UpdateTargetVersion := '{#AppVersion}';
  end;
  Result := SecureUpdateControlRoot;
  if Result <> '' then exit;
  if FileExists(UpdateRequestPath) and not DeleteFile(UpdateRequestPath) then begin
    Result := 'The previous LilacMacro update shutdown request could not be cleared.';
    exit;
  end;
  RequestText := 'schema_version=1' + #13#10
    + 'operation_id=' + UpdateOperationId + #13#10
    + 'target_version=' + UpdateTargetVersion + #13#10
    + 'requested_utc=' + UtcTimestamp + #13#10;
  if not SaveStringToFile(UpdateRequestPath, RequestText, False) then begin
    Result := 'The LilacMacro update shutdown request could not be written.';
    exit;
  end;
  { Give every UI, including runner-session instances, time to observe the shared
    request and flush normally. Cross-account process-handle inspection is not a
    reliable ownership boundary, so the elevated installer then uses the same exact
    four-product-image fallback for coordinated, manual, repair, and legacy updates. }
  Sleep(5000);
  Result := StopManualUpdateProcesses;
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

procedure RelaunchConfiguredRunners;
var
  ResultCode: Integer;
begin
  if IsCoordinatedUpdate or not RunnerJournalExists then exit;
  if not Exec(ExpandConstant('{app}\LilacMacro.SessionSetup.exe'), 'relaunch-runners', '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    Log('The configured local runner UIs could not all be relaunched. Reopen their sessions manually.');
end;

function AttemptRunnerRepair: Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if not RunnerJournalExists then
    exit;
  if not Exec(ExpandConstant('{app}\LilacMacro.SessionSetup.exe'), 'repair', '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then begin
    Result := False;
    Log('Optional local runner migration did not complete. The application upgrade will continue with the runner unavailable until Repair succeeds.');
  end;
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
    RunnerRepairSucceeded := AttemptRunnerRepair;
    if RunnerRepairSucceeded then begin
      RelaunchUpdateParticipants;
      RelaunchConfiguredRunners;
    end else begin
      Log('Configured runner UIs were not relaunched because runner repair failed.');
      DeleteFile(UpdateRequestPath);
    end;
  end;
end;

procedure DeinitializeSetup;
begin
  if UpdateRequestPath <> '' then DeleteFile(UpdateRequestPath);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then begin
    StopUninstallProcesses;
    RequireRunnerCleanup;
  end;
end;
