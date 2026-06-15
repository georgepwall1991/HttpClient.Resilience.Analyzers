$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

function Get-RepositoryPath([string]$relativePath) {
    return Join-Path $repoRoot $relativePath
}

function Get-Text([string]$relativePath) {
    return Get-Content -LiteralPath (Get-RepositoryPath $relativePath) -Raw
}

function Assert-Contains([string]$relativePath, [string]$pattern, [string]$message) {
    $text = Get-Text $relativePath
    if ($text -notmatch $pattern) {
        throw $message
    }
}

$diagnosticIdsText = Get-Text 'src\HttpClient.Resilience.Analyzers\Diagnostics\DiagnosticIds.cs'
$diagnosticIds = [regex]::Matches($diagnosticIdsText, 'public const string (HCR\d{3}) = "\1";') |
    ForEach-Object { $_.Groups[1].Value } |
    Sort-Object

if ($diagnosticIds.Count -eq 0) {
    throw 'No diagnostic IDs were found in DiagnosticIds.cs.'
}

$expectedDiagnosticIds = @(
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
    'HCR063',
    'HCR064',
    'HCR080',
    'HCR081',
    'HCR082',
    'HCR083',
    'HCR084'
)

$unexpectedIds = @($diagnosticIds | Where-Object { $expectedDiagnosticIds -notcontains $_ })
$missingExpectedIds = @($expectedDiagnosticIds | Where-Object { $diagnosticIds -notcontains $_ })

if ($unexpectedIds.Count -gt 0) {
    throw "Unexpected diagnostic IDs declared: $($unexpectedIds -join ', ')."
}

if ($missingExpectedIds.Count -gt 0) {
    throw "Expected diagnostic IDs are missing: $($missingExpectedIds -join ', ')."
}

$profilePaths = @(
    'profiles\default.editorconfig',
    'profiles\brownfield-adoption.editorconfig',
    'profiles\strict-ci.editorconfig',
    'profiles\library-author.editorconfig'
)

foreach ($diagnosticId in $diagnosticIds) {
    if (-not (Test-Path -LiteralPath (Get-RepositoryPath "docs\rules\$diagnosticId.md"))) {
        throw "Missing rule documentation page for $diagnosticId."
    }

    Assert-Contains 'src\HttpClient.Resilience.Analyzers\Diagnostics\DiagnosticDescriptors.cs' `
        "\b$diagnosticId\b" `
        "DiagnosticDescriptors.cs does not mention $diagnosticId."

    Assert-Contains 'src\HttpClient.Resilience.Analyzers\AnalyzerReleases.Unshipped.md' `
        "(?m)^$diagnosticId\s+\|" `
        "AnalyzerReleases.Unshipped.md does not list $diagnosticId."

    Assert-Contains 'docs\implementation-status.md' `
        "\|\s*\x60$diagnosticId\x60\s*\|" `
        "implementation-status.md does not list $diagnosticId."

    Assert-Contains 'README.md' `
        "\x60$diagnosticId\x60" `
        "README.md does not mention $diagnosticId."

    foreach ($profilePath in $profilePaths) {
        Assert-Contains $profilePath `
            "dotnet_diagnostic\.$diagnosticId\.severity\s*=" `
            "$profilePath does not configure $diagnosticId."
    }

    $testMatches = Get-ChildItem -Path (Get-RepositoryPath 'tests') -Recurse -Filter '*.cs' |
        Select-String -Pattern $diagnosticId -List
    if (-not $testMatches) {
        throw "No test file mentions $diagnosticId."
    }

    $ruleDoc = Get-Text "docs\rules\$diagnosticId.md"
    foreach ($section in @('## Why', '## Bad', '## Better', '## Current Detection', '## Suppression', '## References')) {
        if ($ruleDoc -notmatch [regex]::Escape($section)) {
            throw "docs/rules/$diagnosticId.md is missing section '$section'."
        }
    }
}

$requiredTopLevelDocs = @(
    'docs\adoption.md',
    'docs\configuration.md',
    'docs\false-positive-policy.md',
    'docs\implementation-status.md',
    'docs\launch-blog-post.md',
    'docs\releasing.md'
)

foreach ($path in $requiredTopLevelDocs) {
    if (-not (Test-Path -LiteralPath (Get-RepositoryPath $path))) {
        throw "Missing required documentation file: $path."
    }
}

$requiredScripts = @(
    'scripts\Validate-Package.ps1',
    'scripts\Validate-PackageConsumption.ps1',
    'scripts\Validate-Repository.ps1',
    'scripts\Validate-SampleDiagnostics.ps1'
)

foreach ($path in $requiredScripts) {
    if (-not (Test-Path -LiteralPath (Get-RepositoryPath $path))) {
        throw "Missing required validation script: $path."
    }
}

"repository validation ok: $($diagnosticIds -join ', ')"
