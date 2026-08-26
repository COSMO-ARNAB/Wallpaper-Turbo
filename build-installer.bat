@echo off
title Wallpaper Turbo Installer Compiler
for /f "usebackq delims=" %%V in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\get-release-version.ps1"`) do set "RELEASE_VERSION=%%V"
if "%RELEASE_VERSION%"=="" (
    echo [ERROR] Could not resolve the release version from MSBuild.
    exit /b 1
)
echo [INFO] Release version: %RELEASE_VERSION%
echo [1/6] Cleaning up previous artifacts...
if exist publish rmdir /s /q publish
if exist setup rmdir /s /q setup

echo [2/6] Publishing Wallpaper Turbo in Release Mode (Self-Contained win-x64)...
dotnet publish src\WallpaperTurbo.UI\WallpaperTurbo.UI.csproj -c Release -r win-x64 -p:Platform=x64 -p:Version=%RELEASE_VERSION% --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true -o "%~dp0publish\"

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Publishing failed!
    pause
    exit /b %ERRORLEVEL%
)

echo [3/6] Signing published executables...
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0scripts\sign-binaries.ps1" -TargetDir "%~dp0publish" -SkipRootTrust %*
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Code signing of published binaries failed!
    pause
    exit /b %ERRORLEVEL%
)

echo [4/6] Compiling Inno Setup Script...
set "ISCC_CMD=iscc"
where iscc >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    if exist "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" (
        set "ISCC_CMD=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
    ) else if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
        set "ISCC_CMD=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    )
)
"%ISCC_CMD%" /DMyAppVersion=%RELEASE_VERSION% /DUseSignTool /S"mysigntool=""%~dp0scripts\sign-uninstaller.bat"" ""$f""" src\WallpaperTurbo.Installer\installer.iss

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Installation build failed! Ensure Inno Setup is installed and ISCC is in system PATH or local Programs folders.
    pause
    exit /b %ERRORLEVEL%
)

echo [5/6] Signing Setup Installer...
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0scripts\sign-binaries.ps1" -FilePath "%~dp0setup\Wallpaper_Turbo_Setup.exe" -SkipRootTrust %*
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Code signing of the installer package failed!
    pause
    exit /b %ERRORLEVEL%
)

echo [6/6] Generating update.json manifest...
rem The version is supplied explicitly from Directory.Build.props.
powershell -ExecutionPolicy Bypass -NoProfile -File "scripts\build-update-manifest.ps1" -Version "%RELEASE_VERSION%" -InstallerPath "setup\Wallpaper_Turbo_Setup.exe" -OutputPath "setup\update.json"
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] update.json manifest generation failed!
    pause
    exit /b %ERRORLEVEL%
)

powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0scripts\validate-release.ps1" -Version "%RELEASE_VERSION%" -InstallerPath "%~dp0setup\Wallpaper_Turbo_Setup.exe" -ManifestPath "%~dp0setup\update.json" -PublishDir "%~dp0publish"
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Release integrity validation failed!
    pause
    exit /b %ERRORLEVEL%
)

echo [SUCCESS] Setup package compiled and signed successfully at: setup\Wallpaper_Turbo_Setup.exe
echo [SUCCESS] Update manifest written to: setup\update.json
