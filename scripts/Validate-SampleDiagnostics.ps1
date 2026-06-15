param(
    [string]$Configuration = 'Release',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'

$sampleProject = Join-Path $PSScriptRoot '..\samples\HttpClient.Resilience.Showcase\HttpClient.Resilience.Showcase.csproj'
$buildArgs = @(
    'build',
    $sampleProject,
    '--configuration',
    $Configuration,
    '--no-incremental'
)

if ($NoRestore) {
    $buildArgs += '--no-restore'
}

$output = & dotnet @buildArgs 2>&1
$exitCode = $LASTEXITCODE
$text = $output | Out-String

if ($exitCode -ne 0) {
    Write-Output $text
    throw "Sample project build failed with exit code $exitCode."
}

$requiredDiagnostics = @(
    'HCR001',
    'HCR002',
    'HCR003',
    'HCR004',
    'HCR005',
    'HCR020',
    'HCR040',
    'HCR041',
    'HCR060',
    'HCR061',
    'HCR062',
    'HCR080'
)

$missingDiagnostics = @(
    foreach ($diagnostic in $requiredDiagnostics) {
        if ($text -notmatch "\b$diagnostic\b") {
            $diagnostic
        }
    }
)

if ($missingDiagnostics.Count -gt 0) {
    Write-Output $text
    throw "Sample project build output is missing diagnostics: $($missingDiagnostics -join ', ')."
}

"sample diagnostics validation ok: $($requiredDiagnostics -join ', ')"
