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
Name: "hungarian"; MessagesFile: "compiler:Languages\Hungarian.isl"

[CustomMessages]
english.TaskAutostart=Start automatically on login
english.TaskGroupStartup=Startup:
english.TaskInstallService=Install and start the optional system service
english.TaskGroupOptional=Optional components:
english.TaskCleanInstall=Clean install (remove existing settings, API key, and log files)
english.TaskGroupAdvanced=Advanced:
english.StatusFirewall=Configuring firewall...

hungarian.TaskAutostart=Automatikus ind%u00edt%u00e1s bejelentkez%u00e9skor
hungarian.TaskGroupStartup=Ind%u00edt%u00e1s:
hungarian.TaskInstallService=Opcion%u00e1lis rendszerszolg%u00e1ltat%u00e1s telep%u00edt%u00e9se %u00e9s ind%u00edt%u00e1sa
hungarian.TaskGroupOptional=Opcion%u00e1lis %u00f6sszetev%u0151k:
hungarian.TaskCleanInstall=Tiszta telep%u00edt%u00e9s (megl%u00e9v%u0151 be%u00e1ll%u00edt%u00e1sok, API kulcs %u00e9s napl%u00f3f%u00e1jlok t%u00f6rl%u00e9se)
hungarian.TaskGroupAdvanced=Halad%u00f3:
hungarian.StatusFirewall=T%u0171zfal be%u00e1ll%u00edt%u00e1sa...

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "{cm:TaskAutostart}"; GroupDescription: "{cm:TaskGroupStartup}"
Name: "installservice"; Description: "{cm:TaskInstallService}"; GroupDescription: "{cm:TaskGroupOptional}"; Flags: unchecked
Name: "cleaninstall"; Description: "{cm:TaskCleanInstall}"; GroupDescription: "{cm:TaskGroupAdvanced}"; Flags: unchecked

[Files]
Source: "..\artifacts\HASS.Agent.NET10\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{commonappdata}\HASS.Agent.NET10"; Permissions: authusers-modify

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "HASS.Agent.NET10"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#MyAppName} Local API"""; Flags: runhidden waituntilterminated; StatusMsg: "{cm:StatusFirewall}"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""{#MyAppName} Local API"" dir=in action=allow protocol=TCP localport=5115 profile=private program=""{app}\{#MyAppExeName}"" enable=yes"; Flags: runhidden waituntilterminated; StatusMsg: "{cm:StatusFirewall}"

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--stop-service --quiet"; Flags: runhidden waituntilterminated skipifdoesntexist
Filename: "{app}\{#MyAppExeName}"; Parameters: "--uninstall-service --quiet"; Flags: runhidden waituntilterminated skipifdoesntexist
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#MyAppName} Local API"""; Flags: runhidden waituntilterminated

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

    if WizardIsTaskSelected('cleaninstall') then
    begin
      DelTree(ExpandConstant('{commonappdata}\HASS.Agent.NET10'), True, True, True);
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
