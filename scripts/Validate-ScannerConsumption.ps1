param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PackagePath)) {
    throw "Package does not exist: $PackagePath"
}

$resolvedPackage = (Resolve-Path -LiteralPath $PackagePath).Path
$packageDirectory = Split-Path -Parent $resolvedPackage
$packageFileName = [System.IO.Path]::GetFileNameWithoutExtension($resolvedPackage)
$packagePrefix = 'HttpClient.Resilience.Analyzers.Scan.'

if (-not $packageFileName.StartsWith($packagePrefix, [StringComparison]::Ordinal)) {
    throw "Could not infer scanner version from $resolvedPackage."
}

$packageVersion = $packageFileName.Substring($packagePrefix.Length)
if ($packageVersion -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw "Could not infer a stable scanner version from $resolvedPackage."
}

$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('hcr-scan-consume-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $tempDirectory | Out-Null

if (-not $IsWindows) {
    $realpathCommand = Get-Command -CommandType Application realpath -ErrorAction SilentlyContinue
    if ($null -ne $realpathCommand) {
        $canonicalTempDirectory = & $realpathCommand.Source $tempDirectory 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($canonicalTempDirectory)) {
            $tempDirectory = $canonicalTempDirectory.Trim()
        }
    }
}

try {
    $toolPath = Join-Path $tempDirectory 'tools'
    $projectDirectory = Join-Path $tempDirectory 'project'
    $projectPath = Join-Path $projectDirectory 'ScanConsumer.csproj'
    $programPath = Join-Path $projectDirectory 'Program.cs'
    $nugetConfigPath = Join-Path $tempDirectory 'NuGet.Config'
    $sarifPath = Join-Path $tempDirectory 'scan.sarif'

    New-Item -ItemType Directory -Path $projectDirectory | Out-Null

    $escapedFeed = [System.Security.SecurityElement]::Escape($packageDirectory)
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$escapedFeed" />
  </packageSources>
</configuration>
"@ | Set-Content -LiteralPath $nugetConfigPath -Encoding utf8

    $installOutput = & dotnet tool install HttpClient.Resilience.Analyzers.Scan `
        --tool-path $toolPath `
        --version $packageVersion `
        --configfile $nugetConfigPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet tool install failed:`n$($installOutput -join [Environment]::NewLine)"
    }

    $scannerPath = Join-Path $toolPath $(if ($IsWindows) { 'hcr-scan.exe' } else { 'hcr-scan' })
    if (-not (Test-Path -LiteralPath $scannerPath)) {
        throw "The installed tool is missing hcr-scan at $scannerPath."
    }

    $versionOutput = (& $scannerPath --version 2>&1) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0 -or $versionOutput.Trim() -ne "hcr-scan $packageVersion") {
        throw "Installed scanner version did not match $packageVersion. Output: $versionOutput"
    }

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
"@ | Set-Content -LiteralPath $projectPath

    @"
using System.Net.Http;

public static class ClientFactory
{
    public static void Create()
    {
        using var client = new HttpClient();
    }
}
"@ | Set-Content -LiteralPath $programPath

    $scanOutput = & $scannerPath $projectPath `
        --sarif $sarifPath `
        --findings all `
        --no-github 2>&1
    $scanTranscript = $scanOutput -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0) {
        throw "The installed scanner failed with exit code ${LASTEXITCODE}:`n$scanTranscript"
    }

    if ($scanTranscript -notmatch 'HCR Scan found 1 problem' -or $scanTranscript -notmatch 'HCR001') {
        throw "The installed scanner did not report the expected HCR001 finding:`n$scanTranscript"
    }

    if (-not (Test-Path -LiteralPath $sarifPath)) {
        throw 'The installed scanner did not write its SARIF report.'
    }

    $sarif = Get-Content -LiteralPath $sarifPath -Raw | ConvertFrom-Json
    $results = @($sarif.runs[0].results)
    if ($results.Count -ne 1 -or $results[0].ruleId -ne 'HCR001') {
        throw "Expected one HCR001 result in the scanner SARIF report, found $($results.Count)."
    }

    $artifactUri = [string]$results[0].locations[0].physicalLocation.artifactLocation.uri
    if ($artifactUri -ne 'Program.cs') {
        throw "Expected the scanner SARIF path to be relative ('Program.cs') but found '$artifactUri'."
    }
}
finally {
    Remove-Item -LiteralPath $tempDirectory -Recurse -Force
}

'scanner package consumption ok'
