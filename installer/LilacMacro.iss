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
CloseApplications=yes
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
Filename: "{app}\LilacMacro.exe"; Description: "Launch LilacMacro"; Flags: nowait postinstall skipifsilent

[Code]
function RunnerJournalExists: Boolean;
begin
  Result := FileExists(ExpandConstant('{commonappdata}\LilacMacro\Session\provisioning.json'));
end;

procedure RequireRunnerRepair;
var
  ResultCode: Integer;
begin
  if not RunnerJournalExists then
    exit;
  if not Exec(ExpandConstant('{app}\LilacMacro.SessionSetup.exe'), 'repair', '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    RaiseException('The existing local runner could not be migrated. Run REPAIR and retry the upgrade.');
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
    RequireRunnerRepair;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RequireRunnerCleanup;
end;
