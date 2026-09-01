[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet("Install", "Start", "Stop", "Restart", "Uninstall")]
    [string]$Action,

    [ValidatePattern("^[A-Za-z0-9_.-]+$")]
    [string]$ServiceName = "MySqlWebTool",

    [ValidateRange(1, 65535)]
    [int]$Port = 5080
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script as Administrator."
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

switch ($Action) {
    "Install" {
        if ($service) {
            throw "Service '$ServiceName' already exists. Run uninstall-service.bat first to recreate it."
        }

        $appDirectory = [System.IO.Path]::GetFullPath($PSScriptRoot)
        $appDll = Join-Path $appDirectory "MySqlTool.Api.dll"
        if (-not (Test-Path -LiteralPath $appDll -PathType Leaf)) {
            throw "Application file was not found: $appDll"
        }

        $dotnetCommand = Get-Command "dotnet.exe" -ErrorAction SilentlyContinue
        if (-not $dotnetCommand) {
            throw "dotnet.exe was not found. Install ASP.NET Core 8 Runtime first."
        }

        $binaryPath = '"' + $dotnetCommand.Source + '" "' + $appDll +
            '" --urls "http://0.0.0.0:' + $Port + '"'
        New-Service `
            -Name $ServiceName `
            -DisplayName "$ServiceName (MySQL Web Tool)" `
            -Description "MySQL Web Tool ASP.NET Core service" `
            -BinaryPathName $binaryPath `
            -StartupType Automatic | Out-Null

        & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to configure recovery actions for service '$ServiceName'."
        }

        Start-Service -Name $ServiceName
        (Get-Service -Name $ServiceName).WaitForStatus("Running", [TimeSpan]::FromSeconds(30))
        Write-Host "Service '$ServiceName' installed and started on port $Port."
    }

    "Start" {
        if (-not $service) { throw "Service '$ServiceName' does not exist." }
        if ($service.Status -ne "Running") {
            Start-Service -Name $ServiceName
            $service.WaitForStatus("Running", [TimeSpan]::FromSeconds(30))
        }
        Write-Host "Service '$ServiceName' is running."
    }

    "Stop" {
        if (-not $service) { throw "Service '$ServiceName' does not exist." }
        if ($service.Status -ne "Stopped") {
            Stop-Service -Name $ServiceName
            $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
        }
        Write-Host "Service '$ServiceName' is stopped."
    }

    "Restart" {
        if (-not $service) { throw "Service '$ServiceName' does not exist." }
        if ($service.Status -eq "Stopped") {
            Start-Service -Name $ServiceName
        } else {
            Restart-Service -Name $ServiceName -Force
        }
        $service.WaitForStatus("Running", [TimeSpan]::FromSeconds(30))
        Write-Host "Service '$ServiceName' restarted."
    }

    "Uninstall" {
        if (-not $service) {
            Write-Host "Service '$ServiceName' does not exist."
            break
        }
        if ($service.Status -ne "Stopped") {
            Stop-Service -Name $ServiceName
            $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
        }
        & sc.exe delete $ServiceName | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to delete service '$ServiceName'."
        }
        Write-Host "Service '$ServiceName' uninstalled."
    }
}
