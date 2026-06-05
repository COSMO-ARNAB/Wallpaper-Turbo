@echo off
title Wallpaper Turbo Installer Compiler
echo [1/3] Cleaning up previous artifacts...
if exist publish rmdir /s /q publish
if exist setup rmdir /s /q setup

echo [2/3] Publishing Wallpaper Turbo in Release Mode (Self-Contained win-x64)...
dotnet publish src\WallpaperTurbo.UI\WallpaperTurbo.UI.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -o "%~dp0publish\"

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Publishing failed!
    pause
    exit /b %ERRORLEVEL%
)

echo [3/4] Compiling Inno Setup Script...
iscc src\WallpaperTurbo.Installer\installer.iss

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Installation build failed! Ensure Inno Setup is installed and ISCC is in system PATH.
    pause
    exit /b %ERRORLEVEL%
)

echo [4/4] Generating update.json manifest...
rem Version + channel detection lives in scripts\build-update-manifest.ps1
rem (reads installer.iss directly). CMD's `"` parser is too brittle for
rem inline substring extraction, so we delegate the parsing to PowerShell.
powershell -ExecutionPolicy Bypass -NoProfile -File "scripts\build-update-manifest.ps1" -InstallerPath "setup\Wallpaper_Turbo_Setup.exe" -OutputPath "setup\update.json"
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] update.json manifest generation failed!
    pause
    exit /b %ERRORLEVEL%
)

echo [SUCCESS] Setup package compiled successfully at: setup\Wallpaper_Turbo_Setup.exe
echo [SUCCESS] Update manifest written to: setup\update.json
pause
