# Releasing

This repository has a guarded release workflow at `.github/workflows/release.yml`.

## Prerequisites

- Set a repository secret named `NUGET_API_KEY` with permission to push `HttpClient.Resilience.Analyzers`.
- Keep the package version in `src/HttpClient.Resilience.Analyzers.Package/HttpClient.Resilience.Analyzers.Package.csproj` aligned with the intended NuGet version.
- Run the local validation stack before tagging:

```powershell
dotnet restore HttpClient.Resilience.Analyzers.slnx
dotnet format HttpClient.Resilience.Analyzers.slnx --verify-no-changes --exclude samples
dotnet build HttpClient.Resilience.Analyzers.slnx --configuration Release --no-restore
./scripts/Validate-SampleDiagnostics.ps1 -NoRestore
dotnet test HttpClient.Resilience.Analyzers.slnx --configuration Release --no-build --logger trx --results-directory artifacts\test-results
dotnet pack src\HttpClient.Resilience.Analyzers.Package\HttpClient.Resilience.Analyzers.Package.csproj --configuration Release --no-build --output artifacts\packages
$package = Get-ChildItem artifacts\packages\*.nupkg | Sort-Object LastWriteTime -Descending | Select-Object -First 1
./scripts/Validate-Package.ps1 -PackagePath $package.FullName
```

## Preview Release

For `0.1.0-preview.1`, create and push a matching tag:

```powershell
git tag v0.1.0-preview.1
git push origin v0.1.0-preview.1
```

The release workflow will restore, format-check, build, test, pack, validate the package, upload artifacts, and push the `.nupkg` to NuGet.org.

## Dry Run

Use the `Release` workflow's `workflow_dispatch` trigger with `publish` set to `false`. This executes the full build, test, pack, validation, and artifact upload path without publishing.

## Manual Publish

Use `workflow_dispatch` with `publish` set to `true` only when publishing from the current branch is intentional. Tag-based releases are preferred because they leave an immutable version marker.
