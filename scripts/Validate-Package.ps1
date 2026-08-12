param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [string]$ExpectedRepositoryCommit
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PackagePath)) {
    throw "Package does not exist: $PackagePath"
}

$resolvedPackage = (Resolve-Path -LiteralPath $PackagePath).Path
$contents = tar -tf $resolvedPackage
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$packageProjectPath = Join-Path $repoRoot 'src\HttpClient.Resilience.Analyzers.Package\HttpClient.Resilience.Analyzers.Package.csproj'
[xml]$packageProject = Get-Content -LiteralPath $packageProjectPath

function Get-PackageProjectProperty([string]$name) {
    $node = $packageProject.SelectSingleNode("/Project/PropertyGroup/$name")
    if ($null -eq $node) {
        throw "Package project is missing property '$name'."
    }

    return $node.InnerText
}

$requiredPaths = @(
    'analyzers/dotnet/cs/HttpClient.Resilience.Analyzers.dll',
    'LICENSE',
    'README.md',
    'icon.png',
    'assets/icon.png',
    'assets/logo.png',
    'assets/flow-ide-diagnostics.svg',
    'assets/flow-before-after-fix.svg',
    'assets/flow-product-loop.svg',
    'contentFiles/any/any/profiles/default.editorconfig',
    'contentFiles/any/any/profiles/strict-ci.editorconfig',
    'contentFiles/any/any/profiles/brownfield-adoption.editorconfig',
    'contentFiles/any/any/profiles/library-author.editorconfig'
)

foreach ($path in $requiredPaths) {
    if ($contents -notcontains $path) {
        throw "Package is missing $path"
    }
}

# Product-flow visuals referenced by PackageReadmeFile must match on-disk assets.
$visualAssets = @(
    'assets/flow-ide-diagnostics.svg',
    'assets/flow-before-after-fix.svg',
    'assets/flow-product-loop.svg',
    'assets/logo.png',
    'assets/icon.png'
)

$tempAssetDir = Join-Path ([System.IO.Path]::GetTempPath()) ('hcr-assets-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $tempAssetDir | Out-Null
try {
    tar -xf $resolvedPackage -C $tempAssetDir
    foreach ($asset in $visualAssets) {
        $diskPath = Join-Path $repoRoot $asset
        $packedPath = Join-Path $tempAssetDir $asset
        if (-not (Test-Path -LiteralPath $diskPath)) {
            throw "Repository is missing visual asset: $asset"
        }
        if (-not (Test-Path -LiteralPath $packedPath)) {
            throw "Package is missing packed visual asset: $asset"
        }
        $diskHash = (Get-FileHash -LiteralPath $diskPath -Algorithm SHA256).Hash
        $packedHash = (Get-FileHash -LiteralPath $packedPath -Algorithm SHA256).Hash
        if ($diskHash -ne $packedHash) {
            throw "Packed asset does not match repository file: $asset"
        }
    }

    $packedReadme = Join-Path $tempAssetDir 'README.md'
    $sourceReadme = Join-Path $repoRoot 'PACKAGE_README.md'
    $readmeDiskHash = (Get-FileHash -LiteralPath $sourceReadme -Algorithm SHA256).Hash
    $readmePackedHash = (Get-FileHash -LiteralPath $packedReadme -Algorithm SHA256).Hash
    if ($readmeDiskHash -ne $readmePackedHash) {
        throw 'Packed README.md does not match PACKAGE_README.md.'
    }
}
finally {
    Remove-Item -LiteralPath $tempAssetDir -Recurse -Force
}

$libEntries = $contents | Where-Object { $_ -like 'lib/*' }
if ($libEntries) {
    throw "Analyzer package should not contain lib assemblies: $($libEntries -join ', ')"
}

$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('hcr-package-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $tempDirectory | Out-Null

try {
    tar -xf $resolvedPackage -C $tempDirectory
    $nuspec = Get-ChildItem -Path $tempDirectory -Filter '*.nuspec' -Recurse | Select-Object -First 1
    if ($null -eq $nuspec) {
        throw 'Package is missing a .nuspec file.'
    }

    [xml]$xml = Get-Content -LiteralPath $nuspec.FullName
    $namespaceManager = [System.Xml.XmlNamespaceManager]::new($xml.NameTable)
    $namespaceManager.AddNamespace('n', $xml.DocumentElement.NamespaceURI)

    function Get-MetadataText([string]$name) {
        $node = $xml.SelectSingleNode("/n:package/n:metadata/n:$name", $namespaceManager)
        if ($null -eq $node) {
            return $null
        }

        return $node.InnerText
    }

    function Assert-MetadataText([string]$name, [string]$expected) {
        $actual = Get-MetadataText $name
        if ($actual -ne $expected) {
            throw "Expected metadata '$name' to be '$expected' but found '$actual'."
        }
    }

    Assert-MetadataText 'id' (Get-PackageProjectProperty 'PackageId')
    Assert-MetadataText 'version' (Get-PackageProjectProperty 'Version')
    Assert-MetadataText 'title' (Get-PackageProjectProperty 'Title')
    Assert-MetadataText 'authors' (Get-PackageProjectProperty 'Authors')
    Assert-MetadataText 'copyright' (Get-PackageProjectProperty 'Copyright')
    Assert-MetadataText 'description' (Get-PackageProjectProperty 'Description')
    Assert-MetadataText 'icon' (Get-PackageProjectProperty 'PackageIcon')
    Assert-MetadataText 'readme' (Get-PackageProjectProperty 'PackageReadmeFile')
    Assert-MetadataText 'projectUrl' (Get-PackageProjectProperty 'PackageProjectUrl')
    Assert-MetadataText 'releaseNotes' (Get-PackageProjectProperty 'PackageReleaseNotes')
    Assert-MetadataText 'developmentDependency' (Get-PackageProjectProperty 'DevelopmentDependency').ToLowerInvariant()

    $license = $xml.SelectSingleNode('/n:package/n:metadata/n:license', $namespaceManager)
    if ($null -eq $license -or $license.InnerText -ne 'MIT' -or $license.type -ne 'expression') {
        throw 'Package license metadata must use the MIT expression.'
    }

    $repository = $xml.SelectSingleNode('/n:package/n:metadata/n:repository', $namespaceManager)
    if ($null -eq $repository -or $repository.type -ne 'git' -or $repository.url -ne 'https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers') {
        throw 'Package repository metadata is missing or incorrect.'
    }

    $repositoryCommit = [string]$repository.commit
    if ($repositoryCommit -notmatch '^[0-9a-f]{40}$') {
        throw "Package repository commit must be a lowercase 40-character Git SHA, but found '$repositoryCommit'."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedRepositoryCommit) -and
        $repositoryCommit -ne $ExpectedRepositoryCommit.ToLowerInvariant()) {
        throw "Package repository commit '$repositoryCommit' does not match expected commit '$ExpectedRepositoryCommit'."
    }

    $dependencies = $xml.SelectNodes('/n:package/n:metadata/n:dependencies/n:dependency', $namespaceManager)
    if ($dependencies.Count -gt 0) {
        $dependencyIds = @($dependencies | ForEach-Object { $_.id })
        throw "Analyzer package should not declare NuGet dependencies: $($dependencyIds -join ', ')."
    }

    $tags = (Get-MetadataText 'tags') -split '\s+'
    $requiredTags = @(
        'httpclient',
        'ihttpclientfactory',
        'AddHttpClient',
        'resilience',
        'polly',
        'AddStandardResilienceHandler',
        'AddStandardHedgingHandler',
        'hedging',
        'PooledConnectionLifetime',
        'dotnet',
        'csharp',
        'roslyn',
        'roslyn-analyzer',
        'analyzer',
        'analyser',
        'analyzers',
        'aspnetcore',
        'static-analysis',
        'socket-exhaustion',
        'typed-client',
        'retry',
        'resilience-pipeline',
        'dependency-injection',
        'delegatinghandler',
        'socketshttphandler',
        'microsoft-extensions-http-resilience'
    )

    foreach ($tag in $requiredTags) {
        if ($tags -notcontains $tag) {
            throw "Package tags are missing '$tag'."
        }
    }

    # High-intent discoverability terms for NuGet search (description + tags surface).
    $description = Get-MetadataText 'description'
    $title = Get-MetadataText 'title'
    $discoverabilityBlob = "$title $description $((Get-MetadataText 'tags'))"
    $requiredDiscoverabilityTerms = @(
        'IHttpClientFactory',
        'HttpClient',
        'Compile-time',
        'PooledConnectionLifetime',
        'AddStandardResilienceHandler',
        'AddStandardHedgingHandler',
        'Roslyn',
        'Polly'
    )

    foreach ($term in $requiredDiscoverabilityTerms) {
        if ($discoverabilityBlob.IndexOf($term, [StringComparison]::Ordinal) -lt 0) {
            throw "Package metadata is missing discoverability term: $term"
        }
    }
}
finally {
    Remove-Item -LiteralPath $tempDirectory -Recurse -Force
}

'package validation ok'
