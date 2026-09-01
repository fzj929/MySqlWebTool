@echo off
chcp 65001 >nul
setlocal

cd /d "%~dp0"

set "PORT=%1"
if "%PORT%"=="" set "PORT=5080"

echo ============================================
echo  MySQL Web 工具
echo  访问地址: http://localhost:%PORT%
echo  停止服务: Ctrl + C
echo ============================================
echo.

dotnet MySqlTool.Api.dll --urls http://0.0.0.0:%PORT%

endlocal
