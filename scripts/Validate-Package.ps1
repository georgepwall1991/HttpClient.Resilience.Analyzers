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

    Assert-MetadataText 'id' 'HttpClient.Resilience.Analyzers'
    Assert-MetadataText 'version' '0.1.0-preview.1'
    Assert-MetadataText 'authors' 'HttpClient.Resilience.Analyzers contributors'
    Assert-MetadataText 'description' 'Roslyn analyzers for .NET HttpClient, IHttpClientFactory, and Microsoft.Extensions.Http.Resilience. Catches socket exhaustion risks, DNS-stale clients, typed-client lifetime bugs, unsafe retries, handler scope leaks, response disposal mistakes, and fragile outbound HTTP patterns at compile time.'
    Assert-MetadataText 'icon' 'icon.png'
    Assert-MetadataText 'readme' 'README.md'
    Assert-MetadataText 'projectUrl' 'https://github.com/georg-jung/HttpClient.Resilience.Analyzers'
    Assert-MetadataText 'releaseNotes' 'Initial preview with production-safety diagnostics for HttpClient lifetime, typed clients, handlers, resilience retries, response correctness, response disposal, and outbound fan-out.'
    Assert-MetadataText 'developmentDependency' 'true'

    $license = $xml.SelectSingleNode('/n:package/n:metadata/n:license', $namespaceManager)
    if ($null -eq $license -or $license.InnerText -ne 'MIT' -or $license.type -ne 'expression') {
        throw 'Package license metadata must use the MIT expression.'
    }

    $repository = $xml.SelectSingleNode('/n:package/n:metadata/n:repository', $namespaceManager)
    if ($null -eq $repository -or $repository.type -ne 'git' -or $repository.url -ne 'https://github.com/georg-jung/HttpClient.Resilience.Analyzers') {
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
