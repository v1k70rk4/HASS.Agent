#ifndef MyAppVersion
#define MyAppVersion "10.0.0"
#endif

#define MyAppName "HASS.Agent .NET10"
#define MyAppExeName "HASS.Agent.NET10.exe"
#define MyAppPublisher "v1k70rk4"

[Setup]
AppId={{8E71E6C1-B215-4C54-B8A5-A7172D7CF3D2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\artifacts\installer
OutputBaseFilename=HASS.Agent.NET10-Setup-{#MyAppVersion}
SetupIconFile=..\src\HASS.Agent.NET10\Assets\hassagent.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Start automatically on login"; GroupDescription: "Startup:"
Name: "installservice"; Description: "Install and start the optional system service"; GroupDescription: "Optional components:"; Flags: unchecked

[Files]
Source: "..\artifacts\HASS.Agent.NET10\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{commonappdata}\HASS.Agent.NET10"; Permissions: authusers-modify

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "HASS.Agent.NET10"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--stop-service --quiet"; Flags: runhidden waituntilterminated skipifdoesntexist
Filename: "{app}\{#MyAppExeName}"; Parameters: "--uninstall-service --quiet"; Flags: runhidden waituntilterminated skipifdoesntexist

[Code]
var
  ExistingServiceInstalled: Boolean;
  TrayWasRunning: Boolean;

function RunHidden(FileName: string; Parameters: string): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(FileName, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function IsServiceInstalled(): Boolean;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'query "HASS.Agent.NET10.Service"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := ResultCode = 0;
end;

function IsTrayRunning(): Boolean;
var
  ResultCode: Integer;
begin
  Exec(
    ExpandConstant('{sys}\cmd.exe'),
    '/C tasklist /FI "IMAGENAME eq {#MyAppExeName}" /NH | find /I "{#MyAppExeName}" >NUL',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode);
  Result := ResultCode = 0;
end;

procedure EnsureConfigDirectoryPermissions();
begin
  RunHidden(
    ExpandConstant('{sys}\icacls.exe'),
    '"' + ExpandConstant('{commonappdata}\HASS.Agent.NET10') + '" /grant *S-1-5-11:(OI)(CI)M /T /C');
end;

procedure StopInstalledService();
begin
  RunHidden(ExpandConstant('{sys}\sc.exe'), 'stop "HASS.Agent.NET10.Service"');
end;

procedure StopRunningTrayApp();
begin
  if FileExists(ExpandConstant('{app}\{#MyAppExeName}')) then
  begin
    RunHidden(ExpandConstant('{app}\{#MyAppExeName}'), '--exit --quiet');
    Sleep(2000);
    if IsTrayRunning() then
    begin
      RunHidden(ExpandConstant('{sys}\taskkill.exe'), '/IM "{#MyAppExeName}" /T /F');
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    ExistingServiceInstalled := IsServiceInstalled();
    TrayWasRunning := IsTrayRunning();
    StopRunningTrayApp();
    if ExistingServiceInstalled then
    begin
      StopInstalledService();
      Sleep(1500);
    end;
  end;

  if CurStep = ssPostInstall then
  begin
    EnsureConfigDirectoryPermissions();

    if ExistingServiceInstalled or WizardIsTaskSelected('installservice') then
    begin
      RunHidden(ExpandConstant('{app}\{#MyAppExeName}'), '--install-service --quiet');
    end;

    if TrayWasRunning then
    begin
      Exec(ExpandConstant('{app}\{#MyAppExeName}'), '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
    end;
  end;
end;
