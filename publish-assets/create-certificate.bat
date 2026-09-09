@echo off
setlocal
cd /d "%~dp0"
if "%~1"=="" (
  dotnet DataPilot.Api.dll --create-certificate true
) else (
  dotnet DataPilot.Api.dll --create-certificate true --certificate-hosts "%~1"
)
exit /b %ERRORLEVEL%
