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
$packageVersion = $packageFileName -replace '^HttpClient\.Resilience\.Analyzers\.', ''

if ([string]::IsNullOrWhiteSpace($packageVersion) -or $packageVersion -eq $packageFileName) {
    throw "Could not infer package version from $resolvedPackage."
}

$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('hcr-consume-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $tempDirectory | Out-Null

try {
    $projectPath = Join-Path $tempDirectory 'PackageConsumer.csproj'
    $programPath = Join-Path $tempDirectory 'Program.cs'
    $packagesPath = Join-Path $tempDirectory '.packages'

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RestoreSources>$packageDirectory</RestoreSources>
    <RestorePackagesPath>$packagesPath</RestorePackagesPath>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="HttpClient.Resilience.Analyzers" Version="$packageVersion" PrivateAssets="all" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $projectPath

    @"
using System.Net.Http;

public sealed class PaymentService
{
    public async Task<string> SendAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        using var response = await client.GetAsync("https://example.com", cancellationToken);

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
"@ | Set-Content -LiteralPath $programPath

    $restoreOutput = & dotnet restore $projectPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Output ($restoreOutput | Out-String)
        throw "Package consumer restore failed with exit code $LASTEXITCODE."
    }

    $buildOutput = & dotnet build $projectPath --configuration Release --no-restore 2>&1
    $buildExitCode = $LASTEXITCODE
    $buildText = $buildOutput | Out-String

    if ($buildExitCode -ne 0) {
        Write-Output $buildText
        throw "Package consumer build failed with exit code $buildExitCode."
    }

    if ($buildText -notmatch '\bHCR001\b') {
        Write-Output $buildText
        throw 'Package consumer build output did not contain HCR001.'
    }
}
finally {
    Remove-Item -LiteralPath $tempDirectory -Recurse -Force
}

"package consumption validation ok: HttpClient.Resilience.Analyzers $packageVersion emits HCR001"
