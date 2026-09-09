@echo off
chcp 65001 >nul
setlocal

set "ROOT=%~dp0"
set "DOTNET_EXE=C:\Users\Administrator\.dotnet8\dotnet.exe"
if not exist "%DOTNET_EXE%" set "DOTNET_EXE=dotnet"

echo Starting backend : http://localhost:8088
start "DataPilot-Server" cmd /k "cd /d "%ROOT%server" && "%DOTNET_EXE%" run --urls http://localhost:8088"

timeout /t 3 /nobreak >nul

echo Starting frontend: http://localhost:5173
start "DataPilot-Client" cmd /k "cd /d "%ROOT%client" && npm run dev"

echo.
echo Both services are starting in separate windows.
echo Open http://localhost:5173 in your browser.
endlocal
