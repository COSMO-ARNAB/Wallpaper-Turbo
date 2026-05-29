@echo off
title Wallpaper Turbo Installer Compiler
echo [1/3] Cleaning up previous artifacts...
if exist publish rmdir /s /q publish
if exist setup rmdir /s /q setup

echo [2/3] Publishing Wallpaper Turbo in Release Mode (Self-Contained win-x64)...
dotnet publish src\WallpaperTurbo.UI\WallpaperTurbo.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -o publish/

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Publishing failed!
    pause
    exit /b %ERRORLEVEL%
)

echo [3/3] Compiling Inno Setup Script...
iscc src\WallpaperTurbo.Installer\installer.iss

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Installation build failed! Ensure Inno Setup is installed and ISCC is in system PATH.
    pause
    exit /b %ERRORLEVEL%
)

echo [SUCCESS] Setup package compiled successfully at: setup\Wallpaper_Turbo_Setup.exe
pause
