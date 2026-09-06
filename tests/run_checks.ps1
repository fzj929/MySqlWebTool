[CmdletBinding()]
param(
    [string]$Python = "python",
    [string]$BrowserScript = "tests/smoke_browser.py",
    [int]$Port = 5099
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $PSScriptRoot ".artifacts"
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$published = Join-Path $repo "publish/DataPilot"
if (-not (Test-Path (Join-Path $published "DataPilot.Api.dll"))) { throw "Run publish.bat first." }
$previousDirectory = $env:DataPilot__SqliteDirectory
$previousPythonPath = $env:PYTHONPATH
$env:DataPilot__SqliteDirectory = Join-Path $artifacts "data"
$env:PYTHONPATH = (Join-Path $artifacts "python") + [IO.Path]::PathSeparator + $previousPythonPath
$process = $null
try {
    $portProbe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
    try { $portProbe.Start() } finally { $portProbe.Stop() }
    $process = Start-Process -FilePath dotnet -ArgumentList @("DataPilot.Api.dll", "--urls", "http://127.0.0.1:$Port") -WorkingDirectory $published -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $artifacts "server.log") -RedirectStandardError (Join-Path $artifacts "server-error.log")
    $ready = $false
    for ($i = 0; $i -lt 40; $i++) {
        if ($process.HasExited) { throw ("Server exited: " + (Get-Content (Join-Path $artifacts "server-error.log") -Raw)) }
        try {
            $response = Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:$Port/api/health"
            if ($response.StatusCode -eq 200) { $ready = $true; break }
        } catch { Start-Sleep -Milliseconds 250 }
    }
    if (-not $ready) { throw "Server was not ready." }
    & $Python (Join-Path $PSScriptRoot "smoke_api.py") --base "http://127.0.0.1:$Port"
    if ($LASTEXITCODE -ne 0) { throw "API tests failed." }
    if ($BrowserScript) {
        & $Python (Join-Path $repo $BrowserScript) --base "http://127.0.0.1:$Port"
        if ($LASTEXITCODE -ne 0) { throw "Browser tests failed." }
    }
} finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    $env:DataPilot__SqliteDirectory = $previousDirectory
    $env:PYTHONPATH = $previousPythonPath
}
