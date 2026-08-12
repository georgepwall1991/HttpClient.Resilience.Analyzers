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
$package = Get-ChildItem artifacts\packages\*.nupkg | Sort-Object LastWriteTime -Descending | Select-Object -First 1
./scripts/Validate-Package.ps1 -PackagePath $package.FullName
./scripts/Validate-PackageConsumption.ps1 -PackagePath $package.FullName
```

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
`stryker-config.json` (currently 95%). Expand the `mutate` glob when a change introduces a
new rule or shared helper, and add explicit mutant-killer tests for boolean gates, namespace
checks, and method-name filters.

```powershell
./scripts/Invoke-MutationTests.ps1
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
