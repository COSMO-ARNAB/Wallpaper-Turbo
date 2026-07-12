; installer.iss - Inno Setup Script for Wallpaper Turbo
; Packages the self-contained WPF UI & background AppRunner.

#define MyAppName "Wallpaper Turbo"
#define MyAppVersion "1.5.0"
#define MyAppPublisher "COSMO-ARNAB"
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
VersionInfoDescription=Wallpaper Turbo Setup/Uninstaller
VersionInfoProductName=Wallpaper Turbo
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoCopyright=Copyright (C) 2026 {#MyAppPublisher}

; Prevent installation if the app is already running
AppMutex=WallpaperTurbo_AppRunner_Mutex

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

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
; Standard finish-page launch checkbox. Checked by default, user can untick it.
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName} after setup"; Flags: nowait postinstall skipifsilent

[Code]
// No custom relaunch logic; the final page checkbox controls launch directly.
