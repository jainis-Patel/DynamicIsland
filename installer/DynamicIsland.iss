; Inno Setup Script for Dynamic Island for Windows
; Requires Inno Setup 6 (https://jrsoftware.org/isdl.php)
; Compile via command line: iscc.exe DynamicIsland.iss

#define MyAppName "Dynamic Island"
#ifndef MyAppVersion
  #define MyAppVersion "0.9.0"
#endif
#define MyAppPublisher "Jainis Patel"
#define MyAppURL "https://github.com/jainis-Patel/DynamicIsland"
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
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Parameters: "{param:AppParams|}"; Flags: nowait postinstall

[Code]
// Gracefully signal any running Dynamic Island process to close and wait for its file lock to release before upgrading
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  AppPath: String;
  LockTestPath: String;
  Tries: Integer;
begin
  Result := '';
  AppPath := ExpandConstant('{app}\{#MyAppExeName}');
  LockTestPath := AppPath + '.locktest';
  if FileExists(AppPath) then
  begin
    Exec(AppPath, '--stop', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    for Tries := 0 to 24 do
    begin
      if RenameFile(AppPath, LockTestPath) then
      begin
        RenameFile(LockTestPath, AppPath);
        Break;
      end;
      Sleep(200);
    end;
  end;
end;
