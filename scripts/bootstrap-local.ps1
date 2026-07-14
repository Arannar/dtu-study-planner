Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

$pathsToRemove = @(
    (Join-Path $repoRoot "frontend\node_modules"),
    (Join-Path $repoRoot "frontend\.svelte-kit"),
    (Join-Path $repoRoot "frontend\.svelte-kit-local"),
    (Join-Path $repoRoot "backend\bin"),
    (Join-Path $repoRoot "backend\obj"),
    (Join-Path $repoRoot "backend\artifacts"),
    (Join-Path $repoRoot "backend\SoapExplorationData"),
    (Join-Path $repoRoot "artifacts")
)

foreach ($path in $pathsToRemove) {
    if (Test-Path $path) {
        Remove-Item -Recurse -Force $path
    }
}

Push-Location (Join-Path $repoRoot "frontend")
npm install
Pop-Location

Push-Location (Join-Path $repoRoot "backend")
dotnet restore
Pop-Location

Write-Host "Local dependencies restored for $([System.Runtime.InteropServices.RuntimeInformation]::OSDescription)."
