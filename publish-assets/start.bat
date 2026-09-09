@echo off
setlocal

cd /d "%~dp0"
set "PORT=%~1"
if not defined PORT set "PORT=8088"

echo DataPilot: https://localhost:%PORT%
echo Press Ctrl+C to stop.
dotnet DataPilot.Api.dll --environment Production --https-port %PORT%

endlocal
