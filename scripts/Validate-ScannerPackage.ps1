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
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$scannerProjectPath = Join-Path $repoRoot 'src/HttpClient.Resilience.Analyzers.Scan/HttpClient.Resilience.Analyzers.Scan.csproj'
$analyzerProjectPath = Join-Path $repoRoot 'src/HttpClient.Resilience.Analyzers.Package/HttpClient.Resilience.Analyzers.Package.csproj'

[xml]$scannerProject = Get-Content -LiteralPath $scannerProjectPath
[xml]$analyzerProject = Get-Content -LiteralPath $analyzerProjectPath

function Get-ProjectProperty([xml]$project, [string]$name) {
    $node = $project.SelectSingleNode("/Project/PropertyGroup/$name")
    if ($null -eq $node) {
        throw "Project is missing property '$name'."
    }

    return $node.InnerText
}

function Assert-Equal([string]$name, [string]$actual, [string]$expected) {
    if ($actual -ne $expected) {
        throw "Expected $name to be '$expected' but found '$actual'."
    }
}

$expectedVersion = Get-ProjectProperty $analyzerProject 'Version'
$contents = tar -tf $resolvedPackage

$requiredPaths = @(
    'tools/net8.0/any/DotnetToolSettings.xml',
    'tools/net8.0/any/HttpClient.Resilience.Analyzers.Scan.dll',
    'tools/net8.0/any/HttpClient.Resilience.Analyzers.Scan.runtimeconfig.json',
    'tools/net8.0/any/analyzer/HttpClient.Resilience.Analyzers.dll',
    'README.md',
    'icon.png'
)

foreach ($path in $requiredPaths) {
    if ($contents -notcontains $path) {
        throw "Scanner package is missing $path"
    }
}

$libEntries = @($contents | Where-Object { $_ -like 'lib/*' })
if ($libEntries.Count -gt 0) {
    throw "Tool package should not contain lib assemblies: $($libEntries -join ', ')"
}

$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('hcr-scan-package-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $tempDirectory | Out-Null

try {
    tar -xf $resolvedPackage -C $tempDirectory

    $nuspec = Get-ChildItem -Path $tempDirectory -Filter '*.nuspec' -Recurse | Select-Object -First 1
    if ($null -eq $nuspec) {
        throw 'Scanner package is missing a .nuspec file.'
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
            throw "Expected scanner metadata '$name' to be '$expected' but found '$actual'."
        }
    }

    Assert-MetadataText 'id' 'HttpClient.Resilience.Analyzers.Scan'
    Assert-MetadataText 'version' $expectedVersion
    Assert-MetadataText 'title' (Get-ProjectProperty $scannerProject 'Title')
    Assert-MetadataText 'authors' (Get-ProjectProperty $scannerProject 'Authors')
    Assert-MetadataText 'copyright' (Get-ProjectProperty $scannerProject 'Copyright')
    Assert-MetadataText 'description' (Get-ProjectProperty $scannerProject 'Description')
    Assert-MetadataText 'icon' (Get-ProjectProperty $scannerProject 'PackageIcon')
    Assert-MetadataText 'readme' (Get-ProjectProperty $scannerProject 'PackageReadmeFile')
    Assert-MetadataText 'projectUrl' (Get-ProjectProperty $scannerProject 'PackageProjectUrl')

    $license = $xml.SelectSingleNode('/n:package/n:metadata/n:license', $namespaceManager)
    if ($null -eq $license -or $license.InnerText -ne 'MIT' -or $license.type -ne 'expression') {
        throw 'Scanner package license metadata must use the MIT expression.'
    }

    $repository = $xml.SelectSingleNode('/n:package/n:metadata/n:repository', $namespaceManager)
    if ($null -eq $repository -or $repository.type -ne 'git' -or
        $repository.url -ne 'https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers') {
        throw 'Scanner package repository metadata is missing or incorrect.'
    }

    $repositoryCommit = [string]$repository.commit
    if ($repositoryCommit -notmatch '^[0-9a-f]{40}$') {
        throw "Scanner package repository commit must be a lowercase 40-character Git SHA, but found '$repositoryCommit'."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedRepositoryCommit) -and
        $repositoryCommit -ne $ExpectedRepositoryCommit.ToLowerInvariant()) {
        throw "Scanner package repository commit '$repositoryCommit' does not match expected commit '$ExpectedRepositoryCommit'."
    }

    $packageType = $xml.SelectSingleNode('/n:package/n:metadata/n:packageTypes/n:packageType', $namespaceManager)
    if ($null -eq $packageType -or $packageType.name -ne 'DotnetTool') {
        throw 'Scanner package must declare the DotnetTool package type.'
    }

    $dependencies = $xml.SelectNodes('/n:package/n:metadata/n:dependencies/n:dependency', $namespaceManager)
    if ($dependencies.Count -gt 0) {
        $dependencyIds = @($dependencies | ForEach-Object { $_.id })
        throw "Scanner package should not declare NuGet dependencies: $($dependencyIds -join ', ')."
    }

    $tags = (Get-MetadataText 'tags') -split '\s+'
    foreach ($tag in @('httpclient', 'ihttpclientfactory', 'resilience', 'polly', 'dotnet-tool', 'sarif', 'static-analysis', 'false-positives')) {
        if ($tags -notcontains $tag) {
            throw "Scanner package tags are missing '$tag'."
        }
    }

    $title = Get-MetadataText 'title'
    $description = Get-MetadataText 'description'
    if ($title -notmatch 'HCR Scan' -or $description -notmatch 'SARIF' -or $description -notmatch 'HTML') {
        throw 'Scanner package title or description is missing its product or report-format terms.'
    }

    $toolSettingsPath = Join-Path $tempDirectory 'tools/net8.0/any/DotnetToolSettings.xml'
    [xml]$toolSettings = Get-Content -LiteralPath $toolSettingsPath
    $command = $toolSettings.DotNetCliTool.Commands.Command
    if ($null -eq $command -or $command.Name -ne 'hcr-scan' -or $command.Runner -ne 'dotnet' -or
        $command.EntryPoint -ne 'HttpClient.Resilience.Analyzers.Scan.dll') {
        throw 'DotnetToolSettings.xml must expose the hcr-scan command with the scanner entry point.'
    }

    $runtimeConfigPath = Join-Path $tempDirectory 'tools/net8.0/any/HttpClient.Resilience.Analyzers.Scan.runtimeconfig.json'
    $runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
    if ($runtimeConfig.runtimeOptions.tfm -ne 'net8.0' -or
        $runtimeConfig.runtimeOptions.rollForward -ne 'Major') {
        throw 'Scanner runtimeconfig.json must target net8.0 and roll forward to newer major runtimes.'
    }

    $packedReadme = Join-Path $tempDirectory 'README.md'
    $sourceReadme = Join-Path $repoRoot 'src/HttpClient.Resilience.Analyzers.Scan/README.md'
    if ((Get-FileHash -LiteralPath $packedReadme -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $sourceReadme -Algorithm SHA256).Hash) {
        throw 'Packed scanner README.md does not match the scanner README source.'
    }

    $packedIcon = Join-Path $tempDirectory 'icon.png'
    $sourceIcon = Join-Path $repoRoot 'assets/icon.png'
    if ((Get-FileHash -LiteralPath $packedIcon -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $sourceIcon -Algorithm SHA256).Hash) {
        throw 'Packed scanner icon.png does not match assets/icon.png.'
    }

    $builtAnalyzer = Join-Path $repoRoot 'src/HttpClient.Resilience.Analyzers/bin/Release/netstandard2.0/HttpClient.Resilience.Analyzers.dll'
    if (-not (Test-Path -LiteralPath $builtAnalyzer)) {
        throw "Built analyzer is missing: $builtAnalyzer"
    }

    $packedAnalyzer = Join-Path $tempDirectory 'tools/net8.0/any/analyzer/HttpClient.Resilience.Analyzers.dll'
    if ((Get-FileHash -LiteralPath $packedAnalyzer -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $builtAnalyzer -Algorithm SHA256).Hash) {
        throw 'Packed analyzer assembly does not match the Release analyzer build.'
    }

    $builtScanner = Join-Path $repoRoot 'src/HttpClient.Resilience.Analyzers.Scan/bin/Release/net8.0/HttpClient.Resilience.Analyzers.Scan.dll'
    if (-not (Test-Path -LiteralPath $builtScanner)) {
        throw "Built scanner is missing: $builtScanner"
    }

    $packedScanner = Join-Path $tempDirectory 'tools/net8.0/any/HttpClient.Resilience.Analyzers.Scan.dll'
    if ((Get-FileHash -LiteralPath $packedScanner -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $builtScanner -Algorithm SHA256).Hash) {
        throw 'Packed scanner assembly does not match the Release scanner build.'
    }
}
finally {
    Remove-Item -LiteralPath $tempDirectory -Recurse -Force
}

'scanner package validation ok'
