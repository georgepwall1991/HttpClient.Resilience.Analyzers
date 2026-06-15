param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath
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
    Assert-MetadataText 'authors' (Get-PackageProjectProperty 'Authors')
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

    $dependencies = $xml.SelectNodes('/n:package/n:metadata/n:dependencies/n:dependency', $namespaceManager)
    if ($dependencies.Count -gt 0) {
        $dependencyIds = @($dependencies | ForEach-Object { $_.id })
        throw "Analyzer package should not declare NuGet dependencies: $($dependencyIds -join ', ')."
    }

    $tags = (Get-MetadataText 'tags') -split '\s+'
    $requiredTags = @(
        'httpclient',
        'ihttpclientfactory',
        'resilience',
        'polly',
        'dotnet',
        'csharp',
        'roslyn',
        'analyzer',
        'analyser',
        'aspnetcore',
        'static-analysis',
        'socket-exhaustion',
        'typed-client',
        'retry',
        'resilience-pipeline'
    )

    foreach ($tag in $requiredTags) {
        if ($tags -notcontains $tag) {
            throw "Package tags are missing '$tag'."
        }
    }
}
finally {
    Remove-Item -LiteralPath $tempDirectory -Recurse -Force
}

'package validation ok'
