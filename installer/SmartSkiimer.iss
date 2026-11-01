; Smart Skii-mer — installer wizard
; Build: "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\SmartSkiimer.iss
; Output: dist\SmartSkiimer-Setup-<version>.exe  (unsigned — see notes in the build report)

#define AppName "Smart Skii-mer"
#define AppVersion "3.2.0"
#define AppPublisher "GraffGrid"
#define AppExe "Smart Skii-mer.exe"
#define AppId "{{8F2B7C41-2D6E-4E7A-9C3B-5A1D7E9F4B22}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\Smart Skii-mer
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=SmartSkiimer-Setup-{#AppVersion}
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
RestartApplications=no
LicenseFile=LICENSE.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "startup"; Description: "Start {#AppName} with Windows (recommended)"; GroupDescription: "Startup:"
Name: "remotefw"; Description: "Allow my phone to control this PC on my home Wi-Fi (adds a Windows Firewall rule and a local port reservation; requires admin)"; GroupDescription: "Phone remote:"; Flags: unchecked

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\README.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "enable-remote.cmd"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Parameters: "--settings"; IconFilename: "{app}\{#AppExe}"
Name: "{autoprograms}\Enable phone remote"; Filename: "{app}\enable-remote.cmd"; IconFilename: "{app}\{#AppExe}"; Comment: "Allow your phone on this Wi-Fi (needs admin)"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Parameters: "--settings"; IconFilename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Register the phone remote once, with elevation, only if the user opted in.
Filename: "{app}\{#AppExe}"; Parameters: "--install-enable-remote"; Flags: runhidden waituntilterminated; StatusMsg: "Registering phone remote..."; Tasks: remotefw; Check: IsAdminInstallMode
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""Smart Skii-mer Remote"""; Flags: runhidden waituntilterminated; Tasks: remotefw
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""Smart Skii-mer Remote"" dir=in action=allow protocol=TCP localport=47821 profile=private"; Flags: runhidden waituntilterminated; StatusMsg: "Opening the firewall for your home network..."; Tasks: remotefw
Filename: "netsh"; Parameters: "http add urlacl url=http://+:47821/ user={username}"; Flags: runhidden waituntilterminated; Tasks: remotefw
Filename: "{app}\{#AppExe}"; Parameters: "--settings"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#AppExe}"; Parameters: "--tray"; Flags: nowait runhidden; Tasks: startup

[UninstallRun]
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""Smart Skii-mer Remote"""; Flags: runhidden; RunOnceId: "DelFw"
Filename: "netsh"; Parameters: "http delete urlacl url=http://+:47821/"; Flags: runhidden; RunOnceId: "DelAcl"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Smart Skii-mer"; ValueData: """{app}\{#AppExe}"" --tray"; Flags: uninsdeletevalue; Tasks: startup

[Code]
function IsAdminInstallMode: Boolean;
begin
  Result := IsAdmin;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if not IsAdmin and WizardIsTaskSelected('remotefw') then
      MsgBox('The phone remote needs administrator rights once.' + #13#10 +
             'Re-run this installer with "Install for all users" and keep the phone-remote option ticked.' + #13#10 +
             'Smart Skii-mer works fully without it.', mbInformation, MB_OK);
  end;
end;
