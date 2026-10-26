# Contributing

Thanks for helping improve `HttpClient.Resilience.Analyzers`.

This project is intentionally high-signal. A diagnostic should prevent a plausible production failure, avoid surprising false positives, and include documentation that helps teams make the right tradeoff.

## Development Setup

Prerequisites:

- .NET SDK from `global.json`
- PowerShell 7 or later

Restore, build, and test:

```powershell
dotnet restore HttpClient.Resilience.Analyzers.slnx
dotnet build HttpClient.Resilience.Analyzers.slnx --configuration Release --no-restore
dotnet test HttpClient.Resilience.Analyzers.slnx --configuration Release --no-build
```

Run the full local gate before opening a pull request:

```powershell
dotnet format HttpClient.Resilience.Analyzers.slnx --verify-no-changes --exclude samples
dotnet build HttpClient.Resilience.Analyzers.slnx --configuration Release --no-restore
./scripts/Validate-Repository.ps1
./scripts/Validate-SampleDiagnostics.ps1 -NoRestore
dotnet test HttpClient.Resilience.Analyzers.slnx --configuration Release --no-build --logger trx --results-directory artifacts\test-results

dotnet pack src\HttpClient.Resilience.Analyzers.Package\HttpClient.Resilience.Analyzers.Package.csproj --configuration Release --no-build --output artifacts\packages
dotnet pack src\HttpClient.Resilience.Analyzers.Scan\HttpClient.Resilience.Analyzers.Scan.csproj --configuration Release --no-build --output artifacts\packages

$version = ([xml](Get-Content src\HttpClient.Resilience.Analyzers.Package\HttpClient.Resilience.Analyzers.Package.csproj)).Project.PropertyGroup.Version
$analyzerPackage = Join-Path $PWD "artifacts/packages/HttpClient.Resilience.Analyzers.$version.nupkg"
$scannerPackage = Join-Path $PWD "artifacts/packages/HttpClient.Resilience.Analyzers.Scan.$version.nupkg"

./scripts/Validate-Package.ps1 -PackagePath $analyzerPackage
./scripts/Validate-PackageConsumption.ps1 -PackagePath $analyzerPackage
./scripts/Validate-ScannerPackage.ps1 -PackagePath $scannerPackage
./scripts/Validate-ScannerConsumption.ps1 -PackagePath $scannerPackage
```

Name each package explicitly instead of selecting the newest `*.nupkg`: the repository now produces
both the analyzer package and the `hcr-scan` tool, so a wildcard can validate or publish the wrong
artifact.

## Behavior Snapshot

`AnalyzerBehaviorSnapshotTests` runs every analyzer over the corpus in
`tests/HttpClient.Resilience.Analyzers.Tests/Corpus/` and compares the result against the
committed `expected-diagnostics.txt`. Refactoring and performance work is expected to leave
that file untouched, so a diff there is a signal to stop and explain why behavior moved.

When a change intentionally alters diagnostics, regenerate the baseline and review the diff
as part of the pull request:

```powershell
$env:HCR_UPDATE_SNAPSHOTS = '1'
dotnet test HttpClient.Resilience.Analyzers.slnx --configuration Release --filter FullyQualifiedName~AnalyzerBehaviorSnapshotTests
Remove-Item env:HCR_UPDATE_SNAPSHOTS
```

The corpus must keep triggering every shipped rule; a dedicated test fails if one stops
appearing.

## Scan Regression Corpus

`scripts/Invoke-ScanRegression.ps1` scans a pinned set of public .NET repositories in
`tests/scan-corpus/` and fails when a finding appears, disappears, or a repository stops
building the same way. It is the real-world counterpart to the snippet snapshots: the unit
tests say which shapes a rule should catch, and the scan baselines say how the rule behaves
across actual clients, libraries, and framework components.

Run the full gate before a release, or the affected entries while changing a rule:

```powershell
pwsh ./scripts/Invoke-ScanRegression.ps1
pwsh ./scripts/Invoke-ScanRegression.ps1 -Name grpc_grpc-dotnet -Name elastic_elastic-transport-net
```

The first run is slow: it clones the corpus into `artifacts/scan-corpus/repos`, restores
packages, and builds the scanner. Later runs reuse the cache. The full gate is not part of
pull-request CI because it needs network access and a large clone cache.

When analyzer behavior intentionally changes, regenerate only the affected baselines and
review the diff as part of the pull request:

```powershell
pwsh ./scripts/Invoke-ScanRegression.ps1 -UpdateBaselines -Name <name>
git diff tests/scan-corpus/baselines
```

Do not hand-edit a baseline, and do not update one to make a failing run pass without
explaining which shape moved and why the new result is correct. See
[docs/scan-regression.md](docs/scan-regression.md) for the manifest format, drift triage, and
known coverage limits.

## Diagnostic Quality Bar

For a new or expanded diagnostic, include:

- Analyzer coverage for the bad pattern and common safe patterns.
- False-positive tests for lookalike custom APIs when type information is available.
- Documentation under `docs/rules/` with why, bad, better, current detection, suppression, and references.
- A sample project case when the diagnostic is part of the public configured set.
- Release metadata in `AnalyzerReleases.Unshipped.md`.

Code fixes should be conservative and limited to transformations that are obviously safe.

## Mutation Testing

New analyzer logic should keep a Stryker mutation score at or above the break threshold in
`stryker-config.json` (currently 80%). The committed gate covers the new diagnostics and
shared helpers; `stryker-legacy-config.json` preserves the broader historical scope, whose
baseline is still below 80%. Add explicit mutant-killer tests for boolean gates, namespace
checks, and method-name filters, and expand the gate when a change introduces a new rule or
shared helper.

```powershell
./scripts/Invoke-MutationTests.ps1
./scripts/Invoke-MutationTests.ps1 -ConfigFile stryker-legacy-config.json
```

## Documentation Site

The public docs site is MkDocs Material, published to GitHub Pages from `docs/`. Preview locally:

```bash
pip install -r requirements-docs.txt
mkdocs serve
```

`mkdocs build --strict` must succeed before merging documentation or `mkdocs.yml` changes. The Docs workflow runs that check on pull requests.

## Pull Requests

Keep pull requests focused. Prefer one diagnostic or one infrastructure concern per pull request. Include the verification commands you ran and call out any known limitations that remain intentional.
