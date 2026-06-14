; installer.iss - Inno Setup Script for Wallpaper Turbo
; Packages the self-contained WPF UI & background AppRunner.

#define MyAppName "Wallpaper Turbo"
#define MyAppVersion "1.2.5"
#define MyAppPublisher "Arnab"
#define MyAppExeName "WallpaperTurbo.UI.exe"

[Setup]
AppId={{C7D4F608-9486-486A-BA60-DAFF6C2B3381}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\..\setup
OutputBaseFilename=Wallpaper_Turbo_Setup
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\WallpaperTurbo.UI\Assets\Branding\wallpaper-turbo.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesInstallIn64BitMode=x64
CloseApplications=yes
RestartApplications=yes

; Prevent installation if the app is already running
AppMutex=WallpaperTurbo_UI_Mutex,WallpaperTurbo_AppRunner_Mutex

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Start {#MyAppName} automatically on Windows startup"; GroupDescription: "Startup Options:"; Flags: unchecked

[Files]
; Copy all published files from the local publish folder
Source: "..\..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startup

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
; Standard interactive launch checkbox (skipped in silent updates)
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent; Check: not ShouldRestart

; Silent updater relaunch (executed in silent updates when intent is detected)
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: ShouldRestart

[Code]
function ShouldRestart: Boolean;
var
  I: Integer;
  Param: string;
begin
  Result := False;

  for I := 1 to ParamCount do
  begin
    Param := LowerCase(ParamStr(I));

    // Future-proof custom switch
    if Param = '/wt_restart' then
    begin
      Result := True;
      Exit;
    end;

    // Backward-compatible switch for existing v1.2 clients
    if Pos('/mergetasks=', Param) = 1 then
    begin
      if Pos('restart', Param) > 0 then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;
