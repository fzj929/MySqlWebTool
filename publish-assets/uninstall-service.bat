@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0windows-service.ps1" -Action Uninstall %*
exit /b %ERRORLEVEL%
