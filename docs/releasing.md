# Releasing

This repository has a guarded release workflow at `.github/workflows/release.yml`.

## Prerequisites

- Create a NuGet.org Trusted Publishing policy for this repository:
  - Repository owner: `georgepwall1991`
  - Repository: `HttpClient.Resilience.Analyzers`
  - Workflow file: `release.yml`
  - Environment: leave empty unless the workflow is changed to use a GitHub Actions environment.
- Set a repository variable named `NUGET_USER` to the NuGet.org profile name that owns the trusted publishing policy. Use the profile name, not an email address.
- Keep the package version in `src/HttpClient.Resilience.Analyzers.Package/HttpClient.Resilience.Analyzers.Package.csproj` aligned with the intended NuGet version.
- Run the local validation stack before tagging:

```powershell
dotnet restore HttpClient.Resilience.Analyzers.slnx
dotnet format HttpClient.Resilience.Analyzers.slnx --verify-no-changes --exclude samples
dotnet build HttpClient.Resilience.Analyzers.slnx --configuration Release --no-restore
./scripts/Validate-Repository.ps1
./scripts/Validate-SampleDiagnostics.ps1 -NoRestore
dotnet test HttpClient.Resilience.Analyzers.slnx --configuration Release --no-build --logger trx --results-directory artifacts\test-results
dotnet pack src\HttpClient.Resilience.Analyzers.Package\HttpClient.Resilience.Analyzers.Package.csproj --configuration Release --no-build --output artifacts\packages
$package = Get-ChildItem artifacts\packages\*.nupkg | Sort-Object LastWriteTime -Descending | Select-Object -First 1
./scripts/Validate-Package.ps1 -PackagePath $package.FullName
./scripts/Validate-PackageConsumption.ps1 -PackagePath $package.FullName
```

## Preview Release

For `0.1.0-preview.1`, create and push a matching tag:

```powershell
git tag v0.1.0-preview.1
git push origin v0.1.0-preview.1
```

The release workflow will restore, format-check, build, test, pack, validate the package, upload artifacts, and push the `.nupkg` to NuGet.org.
Publishing uses NuGet Trusted Publishing through GitHub OIDC and `NuGet/login@v1`; no long-lived NuGet API key is stored in the repository.

## Dry Run

Use the `Release` workflow's `workflow_dispatch` trigger with `publish` set to `false`. This executes the full build, test, pack, validation, and artifact upload path without publishing.

## Manual Publish

Use `workflow_dispatch` with `publish` set to `true` only when publishing from the current branch is intentional. Tag-based releases are preferred because they leave an immutable version marker.
