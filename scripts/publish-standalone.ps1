param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$frontendRoot = Join-Path $repoRoot "frontend"
$backendRoot = Join-Path $repoRoot "backend"
$frontendBuild = Join-Path $frontendRoot "build"
$backendWwwroot = Join-Path $backendRoot "wwwroot"
$publishRoot = Join-Path $repoRoot "artifacts\publish\$Runtime"

function Assert-NativeSuccess {
    param([string]$CommandName)

    if ($LASTEXITCODE -ne 0) {
        throw "$CommandName failed with exit code $LASTEXITCODE."
    }
}

Push-Location $frontendRoot
npm run build
Assert-NativeSuccess "npm run build"
Pop-Location

if (Test-Path $backendWwwroot) {
    Remove-Item -Recurse -Force $backendWwwroot
}

New-Item -ItemType Directory -Force -Path $backendWwwroot | Out-Null
Copy-Item -Path (Join-Path $frontendBuild "*") -Destination $backendWwwroot -Recurse -Force

Push-Location $backendRoot
dotnet publish backend.csproj `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:UseAppHost=true `
    --output $publishRoot
Assert-NativeSuccess "dotnet publish"
Pop-Location

Write-Host "Standalone package written to $publishRoot"
