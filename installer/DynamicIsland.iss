; Inno Setup Script for Dynamic Island for Windows
; Requires Inno Setup 6 (https://jrsoftware.org/isdl.php)
; Compile via command line: iscc.exe DynamicIsland.iss

#define MyAppName "Dynamic Island"
#define MyAppVersion "0.5.0"
#define MyAppPublisher "Jainis Patel"
#define MyAppURL "https://github.com"
#define MyAppExeName "DynamicIsland.exe"

[Setup]
AppId={{6E48227D-894C-431A-99FE-AC326164287B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\DynamicIsland
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\publish\installer
OutputBaseFilename=DynamicIsland-Setup-Inno
SetupIconFile=app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
CloseApplicationsFilter=*.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Launch Dynamic Island when Windows starts"; GroupDescription: "Startup options:"

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\AllowPhoneBridgeFirewall.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Assets\app.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall

[Code]
// Gracefully signal any running Dynamic Island process to close before upgrading
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  AppPath: String;
begin
  Result := '';
  AppPath := ExpandConstant('{app}\{#MyAppExeName}');
  if FileExists(AppPath) then
  begin
    Exec(AppPath, '--stop', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(250);
  end;
end;
