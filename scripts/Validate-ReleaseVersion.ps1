param(
    [Parameter(Mandatory = $true)]
    [string]$Ref
)

$ErrorActionPreference = 'Stop'

if ($Ref -notmatch '^refs/tags/v') {
    throw "Release ref '$Ref' must be a version tag under refs/tags/v."
}

$packageProjectPath = Join-Path $PSScriptRoot '..\src\HttpClient.Resilience.Analyzers.Package\HttpClient.Resilience.Analyzers.Package.csproj'
[xml]$packageProject = Get-Content -LiteralPath $packageProjectPath -Raw
$packageVersion = [string]($packageProject.Project.PropertyGroup.Version | Select-Object -First 1)

if ([string]::IsNullOrWhiteSpace($packageVersion)) {
    throw 'Package project does not declare a version.'
}

$expectedRef = "refs/tags/v$packageVersion"
if ($Ref -cne $expectedRef) {
    throw "Release ref '$Ref' does not match package version '$packageVersion'; expected '$expectedRef'."
}

"release version validation ok: $Ref"
