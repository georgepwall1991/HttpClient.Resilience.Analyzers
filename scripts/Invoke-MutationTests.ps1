param(
    [int]$Break = 80
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $repoRoot

$manifestPath = Join-Path $repoRoot '.config/dotnet-tools.json'
if (-not (Test-Path $manifestPath)) {
    dotnet new tool-manifest --force | Out-Null
}

$manifest = Get-Content $manifestPath -Raw
if ($manifest -notmatch 'dotnet-stryker') {
    dotnet tool install dotnet-stryker | Out-Null
}

dotnet tool restore | Out-Null
dotnet stryker --config-file stryker-config.json --break-at $Break
exit $LASTEXITCODE
