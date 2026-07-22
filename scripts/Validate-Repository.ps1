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
    '.editorconfig',
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
    'CONTRIBUTING.md',
    'SECURITY.md',
    'SUPPORT.md',
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
    'scripts\Validate-ReleaseVersion.ps1',
    'scripts\Validate-Repository.ps1',
    'scripts\Validate-SampleDiagnostics.ps1'
)

foreach ($path in $requiredScripts) {
    if (-not (Test-Path -LiteralPath (Get-RepositoryPath $path))) {
        throw "Missing required validation script: $path."
    }
}

$requiredRepositoryFiles = @(
    '.gitattributes',
    '.github\CODEOWNERS',
    '.github\dependabot.yml',
    '.github\pull_request_template.md',
    '.github\ISSUE_TEMPLATE\bug_report.yml',
    '.github\ISSUE_TEMPLATE\config.yml',
    '.github\ISSUE_TEMPLATE\false_positive.yml',
    '.github\ISSUE_TEMPLATE\feature_request.yml',
    '.github\workflows\ci.yml',
    '.github\workflows\release.yml'
)

foreach ($path in $requiredRepositoryFiles) {
    if (-not (Test-Path -LiteralPath (Get-RepositoryPath $path))) {
        throw "Missing required repository file: $path."
    }
}

[xml]$packageProject = Get-Text 'src\HttpClient.Resilience.Analyzers.Package\HttpClient.Resilience.Analyzers.Package.csproj'
$packageVersion = [string]($packageProject.Project.PropertyGroup.Version | Select-Object -First 1)
if ($packageVersion -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw "Package version '$packageVersion' must be a stable three-part semantic version."
}

$escapedPackageVersion = [regex]::Escape($packageVersion)
Assert-Contains 'README.md' `
    "<PackageReference Include=\x22HttpClient\.Resilience\.Analyzers\x22 Version=\x22$escapedPackageVersion\x22" `
    "README.md package version does not match package project version $packageVersion."

$releaseDocumentation = Get-Text 'docs\releasing.md'
if ($releaseDocumentation -match '(?i)\bpreview release\b') {
    throw 'docs/releasing.md must describe stable releases only.'
}

Assert-Contains 'docs\releasing.md' '(?m)^## Stable Release\r?$' 'docs/releasing.md must document the stable release process.'

Assert-Contains '.gitattributes' '\*\s+text=auto\s+eol=crlf' '.gitattributes must normalize text files to CRLF.'
Assert-Contains '.github\CODEOWNERS' '@georgepwall1991' 'CODEOWNERS must include the repository owner.'
Assert-Contains '.github\dependabot.yml' 'package-ecosystem:\s+nuget' 'dependabot.yml must include NuGet updates.'
Assert-Contains '.github\dependabot.yml' 'package-ecosystem:\s+github-actions' 'dependabot.yml must include GitHub Actions updates.'
Assert-Contains '.github\pull_request_template.md' 'Validate-Repository\.ps1' 'Pull request template must include repository validation.'
Assert-Contains '.github\workflows\release.yml' 'Validate-ReleaseVersion\.ps1' 'Release workflow must validate tag and package version alignment.'
Assert-Contains 'SECURITY.md' 'Reporting a Vulnerability' 'SECURITY.md must document vulnerability reporting.'
Assert-Contains 'CONTRIBUTING.md' 'Diagnostic Quality Bar' 'CONTRIBUTING.md must document diagnostic quality expectations.'
Assert-Contains 'SUPPORT.md' 'False positives' 'SUPPORT.md must document support paths for false positives.'

"repository validation ok: $($diagnosticIds -join ', ')"
