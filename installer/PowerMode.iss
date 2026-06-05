; Power Mode installer
; Build with Inno Setup 6:
;   Build-Release-Installer.cmd

#define AppName "Power Mode"
#define AppVersion "2.7.16-beta.1"
#define AppVersionNumeric "2.7.16.1"
#define AppPublisher "KomCom"
#define AppExeName "PowerModeTray.exe"
#define SourceRoot ".."
#define PublishDir SourceRoot + "\winui\publish"

[Setup]
AppId={{F1A23D90-6D57-49DF-B8B8-A9058D25E0D1}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\Power Mode
DefaultGroupName=Power Mode
DisableProgramGroupPage=yes
OutputDir={#SourceRoot}\dist
OutputBaseFilename=PowerModeSetup-v{#AppVersion}
VersionInfoVersion={#AppVersionNumeric}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersionNumeric}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoTextVersion={#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dynamic windows11 hidebevels
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile={#SourceRoot}\native\assets\PowerModeTray.ico
WizardImageFile={#SourceRoot}\installer\assets\wizard-image-light.png
WizardImageFileDynamicDark={#SourceRoot}\installer\assets\wizard-image-dark.png
WizardSmallImageFile={#SourceRoot}\installer\assets\wizard-small-light.png
WizardSmallImageFileDynamicDark={#SourceRoot}\installer\assets\wizard-small-dark.png
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
ChangesAssociations=no
ChangesEnvironment=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\scripts\Install-Dependencies.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "{#SourceRoot}\scripts\Install-Dependencies.ps1"; DestName: "Install-Dependencies-Prepare.ps1"; Flags: dontcopy
Source: "{#SourceRoot}\scripts\Install-PowerModeRelease.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "{#SourceRoot}\scripts\Ensure-MicaPowerPlans.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "{#SourceRoot}\config\power-plans.json"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "{#SourceRoot}\scripts\Install-PowerModeTrayStartup.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "{#SourceRoot}\scripts\Remove-PowerModeTrayStartup.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "{#SourceRoot}\scripts\Restore-PowerModeGlobalBehavior.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "{#SourceRoot}\scripts\Stop-PowerModeTray.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "{#SourceRoot}\scripts\Promote-PowerModeTrayIcon.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "{#SourceRoot}\scripts\Repair-PowerPlans-Admin.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "{#SourceRoot}\PowerMode-QuickGuide.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#SourceRoot}\PowerMode-QuickGuide.txt"; DestDir: "{app}\docs"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Power Mode"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"
Name: "{autoprograms}\Repair Power Mode power profiles"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Repair-PowerPlans-Admin.ps1"" -SetOptimizedActive"; WorkingDir: "{app}"

[Code]
var
  RestorePowerPlansOnUninstall: Boolean;
  DependencyPage: TOutputProgressWizardPage;
  DownloadProgressBase: Integer;
  DownloadProgressRange: Integer;

function PowerShellPath(): string;
begin
  Result := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
end;

function DependencyLogPath(): string;
begin
  Result := ExpandConstant('{commonappdata}\MicaLovesKPOP\PowerMode\logs\dependency-install.log');
end;

function PrepareLogFolder(): Boolean;
begin
  Result := ForceDirectories(ExpandConstant('{commonappdata}\MicaLovesKPOP\PowerMode\logs'));
end;

function RunPowerShellHidden(Params: string; WorkingDir: string; var ResultCode: Integer): Boolean;
begin
  Result := Exec(PowerShellPath(), Params, WorkingDir, SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function OnDependencyDownloadProgress(const Url, Filename: String; const Progress, ProgressMax: Int64): Boolean;
var
  CurrentProgress: Integer;
begin
  if ProgressMax > 0 then
  begin
    CurrentProgress := DownloadProgressBase + Integer((Progress * DownloadProgressRange) / ProgressMax);
    if CurrentProgress > DownloadProgressBase + DownloadProgressRange then
      CurrentProgress := DownloadProgressBase + DownloadProgressRange;
    DependencyPage.SetProgress(CurrentProgress, 100);
  end;
  Result := True;
end;

function IsDependencyPresent(CheckSwitch: String): Boolean;
var
  ResultCode: Integer;
  Params: string;
begin
  ExtractTemporaryFile('Install-Dependencies-Prepare.ps1');

  Params :=
    '-NoProfile -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{tmp}\Install-Dependencies-Prepare.ps1') +
    '" ' + CheckSwitch +
    ' -LogPath "' + DependencyLogPath() + '"';

  if not RunPowerShellHidden(Params, ExpandConstant('{tmp}'), ResultCode) then
  begin
    Result := False;
    Exit;
  end;

  Result := ResultCode = 0;
end;

function InstallDependencyWithPowerShell(InstallSwitch: String; InstallerPathSwitch: String; InstallerPath: String): String;
var
  ResultCode: Integer;
  Params: string;
begin
  Result := '';

  Params :=
    '-NoProfile -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{tmp}\Install-Dependencies-Prepare.ps1') +
    '" -Silent ' + InstallSwitch +
    ' ' + InstallerPathSwitch + ' "' + InstallerPath + '"' +
    ' -LogPath "' + DependencyLogPath() + '"';

  if not RunPowerShellHidden(Params, ExpandConstant('{tmp}'), ResultCode) then
  begin
    Result := 'Power Mode could not start a required Microsoft component installer.';
  end
  else if ResultCode <> 0 then
  begin
    Result :=
      'Power Mode could not install or verify a required Microsoft component.' + #13#10 + #13#10 +
      'Log file:' + #13#10 +
      DependencyLogPath();
  end;
end;

function VerifyAllDependenciesReady(): String;
var
  ResultCode: Integer;
  Params: string;
begin
  Result := '';

  Params :=
    '-NoProfile -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{tmp}\Install-Dependencies-Prepare.ps1') +
    '" -VerifyOnly -LogPath "' + DependencyLogPath() + '"';

  if not RunPowerShellHidden(Params, ExpandConstant('{tmp}'), ResultCode) then
  begin
    Result := 'Power Mode could not start the final dependency verification step.';
  end
  else if ResultCode <> 0 then
  begin
    Result :=
      'Power Mode could not verify all required Microsoft components.' + #13#10 + #13#10 +
      'Log file:' + #13#10 +
      DependencyLogPath();
  end;
end;

procedure DownloadDependency(Url: String; FileName: String; ProgressBase: Integer; ProgressRange: Integer);
begin
  DownloadProgressBase := ProgressBase;
  DownloadProgressRange := ProgressRange;
  DownloadTemporaryFile(Url, FileName, '', @OnDependencyDownloadProgress);
  DependencyPage.SetProgress(ProgressBase + ProgressRange, 100);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  DotNetInstallerPath: string;
  WindowsAppRuntimeInstallerPath: string;
begin
  Result := '';
  PrepareLogFolder();

  DependencyPage := CreateOutputProgressPage(
    'Preparing Power Mode',
    'Installing required Microsoft components.');
  DependencyPage.Show;
  try
    ExtractTemporaryFile('Install-Dependencies-Prepare.ps1');

    DependencyPage.SetText(
      'Checking Microsoft .NET Desktop Runtime...',
      ' ');
    DependencyPage.SetProgress(5, 100);

    if not IsDependencyPresent('-CheckDotNetOnly') then
    begin
      DotNetInstallerPath := ExpandConstant('{tmp}\windowsdesktop-runtime-8-x64.exe');

      DependencyPage.SetText(
        'Downloading Microsoft .NET Desktop Runtime...',
        ' ');
      DependencyPage.SetProgress(10, 100);
      DownloadDependency(
        'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe',
        'windowsdesktop-runtime-8-x64.exe',
        10,
        25);

      DependencyPage.SetText(
        'Installing Microsoft .NET Desktop Runtime...',
        ' ');
      DependencyPage.SetProgress(38, 100);
      Result := InstallDependencyWithPowerShell('-InstallDotNetOnly', '-DotNetInstallerPath', DotNetInstallerPath);
      if Result <> '' then Exit;
      DependencyPage.SetProgress(45, 100);
    end
    else
    begin
      DependencyPage.SetText('Microsoft .NET Desktop Runtime is already installed.', ' ');
      DependencyPage.SetProgress(45, 100);
    end;

    DependencyPage.SetText(
      'Checking Microsoft Windows App Runtime 1.6...',
      ' ');
    DependencyPage.SetProgress(48, 100);

    if not IsDependencyPresent('-CheckWindowsAppRuntimeOnly') then
    begin
      WindowsAppRuntimeInstallerPath := ExpandConstant('{tmp}\windowsappruntimeinstall-1.6-x64.exe');

      DependencyPage.SetText(
        'Downloading Microsoft Windows App Runtime 1.6...',
        ' ');
      DependencyPage.SetProgress(52, 100);
      DownloadDependency(
        'https://aka.ms/windowsappsdk/1.6/latest/windowsappruntimeinstall-x64.exe',
        'windowsappruntimeinstall-1.6-x64.exe',
        52,
        25);

      DependencyPage.SetText(
        'Installing Microsoft Windows App Runtime 1.6...',
        ' ');
      DependencyPage.SetProgress(80, 100);
      Result := InstallDependencyWithPowerShell('-InstallWindowsAppRuntimeOnly', '-WindowsAppRuntimeInstallerPath', WindowsAppRuntimeInstallerPath);
      if Result <> '' then Exit;
      DependencyPage.SetProgress(90, 100);
    end
    else
    begin
      DependencyPage.SetText('Microsoft Windows App Runtime 1.6 is already installed.', ' ');
      DependencyPage.SetProgress(90, 100);
    end;

    DependencyPage.SetText(
      'Verifying required Microsoft components...',
      ' ');
    DependencyPage.SetProgress(94, 100);
    Result := VerifyAllDependenciesReady();
    if Result <> '' then Exit;

    DependencyPage.SetText('Required Microsoft components are ready.', ' ');
    DependencyPage.SetProgress(100, 100);
  finally
    DependencyPage.Hide;
  end;
end;

procedure StopRunningPowerModeBeforeFileCopy();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#AppExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure RunPowerModePostInstall();
var
  ResultCode: Integer;
  Params: string;
  Page: TOutputProgressWizardPage;
begin
  Page := CreateOutputProgressPage(
    'Finishing Power Mode setup',
    'Applying settings and starting the app.');
  Page.Show;
  try
    Page.SetText('Applying Power Mode settings...', ' ');
    Page.SetProgress(70, 100);

    Params :=
      '-NoProfile -ExecutionPolicy Bypass -File "' +
      ExpandConstant('{app}\scripts\Install-PowerModeRelease.ps1') +
      '" -ExePath "' +
      ExpandConstant('{app}\{#AppExeName}') +
      '" -SkipDependencyCheck';

    if not RunPowerShellHidden(Params, ExpandConstant('{app}'), ResultCode) then
    begin
      MsgBox('Power Mode was copied, but the final setup step could not be started.', mbError, MB_OK);
    end
    else if ResultCode <> 0 then
    begin
      MsgBox(
        'Power Mode was copied, but final setup did not complete successfully.' + #13#10 + #13#10 +
        'You can try repairing the Power Mode profiles from the Start menu, or reinstall Power Mode.' + #13#10 + #13#10 +
        'Logs are stored under:' + #13#10 +
        'C:\ProgramData\MicaLovesKPOP\PowerMode\logs',
        mbError,
        MB_OK);
    end;

    Page.SetProgress(100, 100);
    Page.SetText('Power Mode setup completed.', ' ');
  finally
    Page.Hide;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    StopRunningPowerModeBeforeFileCopy();

  if CurStep = ssPostInstall then
    RunPowerModePostInstall();
end;

function InitializeUninstall(): Boolean;
begin
  RestorePowerPlansOnUninstall :=
    MsgBox(
      'Do you also want to restore the standard Windows power plans?' + #13#10 + #13#10 +
      'This removes all custom power plans on this PC, not only Power Mode profiles.' + #13#10 + #13#10 +
      'Choose No if you only want to uninstall the app.',
      mbConfirmation,
      MB_YESNO) = IDYES;

  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  Params: string;
begin
  if CurUninstallStep = usUninstall then
  begin
    Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\scripts\Stop-PowerModeTray.ps1') + '"';
    Exec(PowerShellPath(), Params, ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode);

    Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\scripts\Remove-PowerModeTrayStartup.ps1') + '"';
    Exec(PowerShellPath(), Params, ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode);

    Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\scripts\Restore-PowerModeGlobalBehavior.ps1') + '"';
    Exec(PowerShellPath(), Params, ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode);

    if RestorePowerPlansOnUninstall then
    begin
      Exec(ExpandConstant('{sys}\powercfg.exe'), '/restoredefaultschemes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Exec(ExpandConstant('{sys}\powercfg.exe'), '/setactive 381b4222-f694-41f0-9685-ff5bb260df2e', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;
