@echo off
setlocal

cd /d "%~dp0"
set "PORT=%~1"
if not defined PORT set "PORT=5080"

echo DataPilot: http://localhost:%PORT%
echo Press Ctrl+C to stop.
dotnet DataPilot.Api.dll --urls http://0.0.0.0:%PORT%

endlocal
