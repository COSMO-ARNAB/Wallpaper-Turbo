@echo off
rem Helper script for Inno Setup SignTool to sign uninst.e32 cleanly without space escaping issues
powershell.exe -ExecutionPolicy Bypass -NoProfile -File "%~dp0sign-binaries.ps1" -FilePath "%~1" -SkipRootTrust
