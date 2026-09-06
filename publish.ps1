[CmdletBinding()]
param(
    [string]$OutputDirectory = "publish/DataPilot",
    [string]$RuntimeIdentifier = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$clientRoot = Join-Path $projectRoot "client"
$serverProject = Join-Path $projectRoot "server/DataPilot.Api.csproj"
$allowedLocalOutputRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot "publish"))
$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
}

if ($outputRoot.TrimEnd('\', '/') -eq $projectRoot.TrimEnd('\', '/')) {
    throw "The publish directory cannot be the project root."
}

$pathComparison = [System.StringComparison]::OrdinalIgnoreCase
$projectPrefix = $projectRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
$allowedPrefix = $allowedLocalOutputRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
$isInsideProject = $outputRoot.StartsWith($projectPrefix, $pathComparison)
$isAllowedLocalOutput = $outputRoot.TrimEnd('\', '/') -eq $allowedLocalOutputRoot.TrimEnd('\', '/') -or
    $outputRoot.StartsWith($allowedPrefix, $pathComparison)
if ($isInsideProject -and -not $isAllowedLocalOutput) {
    throw "A publish directory inside the project must be under: $allowedLocalOutputRoot"
}
if (Test-Path -LiteralPath $outputRoot) {
    if (-not (Test-Path -LiteralPath (Join-Path $outputRoot ".datapilot-release"))) {
        throw "The output directory already exists and is not a DataPilot release. Choose a new empty path."
    }
    if ((Get-Item -LiteralPath $outputRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "The output directory cannot be a symbolic link."
    }
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$WorkingDirectory
    )

    Push-Location $WorkingDirectory
    try {
        & $Command @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Command failed with exit code $LASTEXITCODE`: $Command $($Arguments -join ' ')"
        }
    } finally {
        Pop-Location
    }
}

$npmCommandInfo = Get-Command "npm.cmd" -ErrorAction SilentlyContinue
if (-not $npmCommandInfo) {
    $npmCommandInfo = Get-Command "npm" -ErrorAction SilentlyContinue
}
$npmCommand = if ($npmCommandInfo) { $npmCommandInfo.Source } else { "npm" }
$dotnetCommand = "dotnet"

if (-not $npmCommandInfo) {
    throw "npm was not found. Install Node.js on the build machine."
}
if (-not (Get-Command $dotnetCommand -ErrorAction SilentlyContinue)) {
    throw "dotnet was not found. Install the .NET 8 SDK on the build machine."
}

$stagingRoot = Join-Path $projectRoot ".publish-tmp/$([Guid]::NewGuid().ToString('N'))"

try {
    Write-Host "[1/4] Installing frontend dependencies..." -ForegroundColor Cyan
    Invoke-CheckedCommand -Command $npmCommand -Arguments @("install", "--no-audit", "--no-fund") -WorkingDirectory $clientRoot

    Write-Host "[2/4] Building frontend..." -ForegroundColor Cyan
    Invoke-CheckedCommand -Command $npmCommand -Arguments @("run", "build") -WorkingDirectory $clientRoot

    Write-Host "[3/4] Publishing .NET 8 backend..." -ForegroundColor Cyan
    $publishArguments = @(
        "publish",
        $serverProject,
        "--configuration", "Release",
        "--output", $stagingRoot,
        "--no-self-contained"
    )
    if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
        $publishArguments += @("--runtime", $RuntimeIdentifier)
    }
    Invoke-CheckedCommand -Command $dotnetCommand -Arguments $publishArguments -WorkingDirectory $projectRoot

    Write-Host "[4/4] Combining frontend and launch scripts..." -ForegroundColor Cyan
    $webRoot = Join-Path $stagingRoot "wwwroot"
    New-Item -ItemType Directory -Path $webRoot -Force | Out-Null
    Copy-Item -Path (Join-Path $clientRoot "dist/*") -Destination $webRoot -Recurse -Force
    Copy-Item -Path (Join-Path $projectRoot "publish-assets/*") -Destination $stagingRoot -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot "README.md") -Destination $stagingRoot
    [IO.File]::WriteAllText((Join-Path $stagingRoot ".datapilot-release"), "DataPilot", [Text.UTF8Encoding]::new($false))
    foreach ($script in Get-ChildItem -LiteralPath $stagingRoot -Filter "*.sh") {
        [IO.File]::WriteAllText($script.FullName, ([IO.File]::ReadAllText($script.FullName)).Replace("`r`n", "`n"), [Text.UTF8Encoding]::new($false))
    }

    if (Test-Path -LiteralPath $outputRoot) {
        $backupPath = $outputRoot + ".previous-" + [Guid]::NewGuid().ToString("N")
        Move-Item -LiteralPath $outputRoot -Destination $backupPath
        Write-Host "Previous release preserved: $backupPath"
    }
    $outputParent = Split-Path -Parent $outputRoot
    New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
    Move-Item -LiteralPath $stagingRoot -Destination $outputRoot

    Write-Host ""
    Write-Host "Publish completed: $outputRoot" -ForegroundColor Green
    Write-Host "Copy the entire directory to a host with ASP.NET Core 8 Runtime, then run:"
    Write-Host "  Windows: start.bat [port]"
    Write-Host "  Linux:   chmod +x start.sh && ./start.sh [port]"
} finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }

    $stagingParent = Split-Path -Parent $stagingRoot
    if ((Test-Path -LiteralPath $stagingParent) -and
        -not (Get-ChildItem -LiteralPath $stagingParent -Force | Select-Object -First 1)) {
        Remove-Item -LiteralPath $stagingParent -Force
    }
}
