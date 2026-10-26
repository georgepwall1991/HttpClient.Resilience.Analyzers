---
title: Scan regression corpus
description: Pin public .NET repositories and fail when HttpClient analyzer findings change without an explained baseline update.
---

# Scan Regression Corpus

The analyzer tests prove individual rules against small snippets. The scan corpus checks the
same rules against real, public .NET repositories. It is a **drift detector**, not a correctness
oracle: every finding in every pinned repository is stored in a baseline, and the regression
script fails when a finding appears, disappears, or a repository stops building the same way.

This is how a precision change is proved. A suppression that clears a finding shows up as a
removed baseline line; a widened rule shows up as an added line. Either is fine when it is
intentional, reviewed, and explained. Neither is fine when it happens silently.

## Run the gate

Requirements: the .NET SDK from `global.json`, PowerShell 7 or later, `git`, and network access
for the first clone and package restore.

```powershell
pwsh ./scripts/Invoke-ScanRegression.ps1
```

The script:

1. builds `hcr-scan` from the working tree in Release;
2. clones or updates each repository in `tests/scan-corpus/corpus.json` at its pinned commit;
3. scans the configured solution or project with `--findings all --no-github --sarif`;
4. compares the normalized findings with `tests/scan-corpus/baselines/<name>.txt`.

The first run downloads the corpus and restores packages, so it is slow. Later runs reuse the
clone cache, though the scanner rebuilds each target so the analyzers always run. Treat the
cache as disposable: the script force-checks-out the pinned commit in every clone.

| Option | Effect |
|---|---|
| `-Name <name>` | Run only one corpus entry. Repeat it to select several. |
| `-CachePath <path>` | Keep clones somewhere other than `artifacts/scan-corpus/repos`, or reuse an existing cache. |
| `-NoBuild` | Reuse the scanner already in `src/HttpClient.Resilience.Analyzers.Scan/bin/Release`. |
| `-UpdateBaselines` | Rewrite the selected baselines instead of failing on drift. Review the diff before committing. |

Examples:

```powershell
# One repository while iterating on a rule
pwsh ./scripts/Invoke-ScanRegression.ps1 -Name grpc_grpc-dotnet

# Reuse a cache from an earlier run
pwsh ./scripts/Invoke-ScanRegression.ps1 -CachePath /path/to/hcr-corpus

# Prove the current scanner after a behavior change
pwsh ./scripts/Invoke-ScanRegression.ps1 -UpdateBaselines -Name elastic_elastic-transport-net
git diff tests/scan-corpus/baselines/elastic_elastic-transport-net.txt
```

## When the gate fails

An **added** line (`+`) is a new or widened diagnostic. Decide whether it is a true positive, a
false positive, or a case the rule should stay quiet on.

A **removed** line (`-`) is a suppression or lost coverage. Confirm that the analyzer still
reports the bad pattern in its unit tests and that the removal is explained by the change, not
by a build that stopped compiling the project.

An **exit code** mismatch means the repository no longer builds the way the manifest expects.
Inspect `artifacts/scan-corpus/logs/<name>.log`; do not update the baseline until the build is
understood.

If the drift is intentional:

1. Run the script with `-UpdateBaselines` (add `-Name` to limit the update).
2. Review every changed baseline line with `git diff tests/scan-corpus/baselines`.
3. Explain the change in the pull request: which rule moved, which shape caused it, and why the
   new behavior is correct.
4. Commit the manifest and baseline changes together.

Never hand-edit a baseline. The script is the only supported writer because it also records the
repository, commit, target, and scanner exit code. `Validate-Repository.ps1` cross-checks those
headers and the baseline inventory on every pull request, so a stale manifest entry fails even
when the full scan is not run.

## Baseline format

```text
# HCR scan regression baseline. Regenerate with scripts/Invoke-ScanRegression.ps1 -UpdateBaselines.
# repository: elastic/elastic-transport-net
# commit: bc983eab5296ff61cfe265e06b6234953e6686b2
# target: Elastic.Transport.slnx
# exit: 0
HCR060 src/Elastic.Transport/Components/TransportClient/HttpRequestInvoker.cs:132:6
HCR081 src/Elastic.Transport/Components/TransportClient/HttpRequestInvoker.cs:153:17
```

Each finding is `ruleId repoRelativePath:line:column`, using one-based line and column numbers.
Lines are sorted ordinally. The header records what produced the baseline so a changed manifest
entry is detected instead of silently comparing unrelated results.

## Manifest

`tests/scan-corpus/corpus.json` pins each repository:

| Field | Meaning |
|---|---|
| `name` | Stable cache directory and baseline filename. |
| `repository` | `owner/repo` slug recorded in the baseline header. |
| `url` | Clone URL. |
| `commit` | Full lowercase commit SHA. Never use a branch or tag. |
| `target` | Solution or project path relative to the repository root. |
| `framework` | Optional target framework passed to the scanner as `-f`. |
| `expectedExitCode` | Scanner exit code the baseline expects; `0` unless the build is intentionally partial. |
| `notes` | Why the entry pins a particular target or expects a non-zero exit code. |

The `excluded` section records repositories that were evaluated but cannot provide a stable
baseline, with the reason and the commit that was tried. It is documentation, not a skip list
the script reads.

To add a repository:

1. Add the manifest entry at a full commit SHA.
2. Run `pwsh ./scripts/Invoke-ScanRegression.ps1 -UpdateBaselines -Name <name>`.
3. Review the baseline and the scan log. Keep the entry only when the result is stable and the
   repository exercises code the existing corpus does not.
4. Commit the manifest and the new baseline.

## Scope and limits

- The corpus is **library-heavy**. It contains SDKs, clients, and framework components, but not
  large ASP.NET applications, MAUI or Unity apps, or long-running workers. Passing the corpus
  does not prove an app-shaped codebase is clean.
- `dapr/dotnet-sdk` is scanned at `src/Dapr.Actors/Dapr.Actors.csproj` rather than `all.sln`
  because the solution's test projects build unreliably under the scanner. The project graph
  still covers the actor, client, and common projects. `expectedExitCode` exists for a target
  whose build is genuinely partial, but prefer a narrower target over a flaky one.
- `getsentry/sentry-dotnet`, `microsoft/reverse-proxy`, `reactiveui/refit`, and
  `simplcommerce/SimplCommerce` are excluded because their targets did not build under the
  scanner with the repository's documented SDK, or require an SDK that is not part of the
  toolchain. The manifest records the commit and reason for each.
- Results depend on the SDK selected by the corpus repository's `global.json`, available
  package feeds, and the machine. Some repositories pin an exact SDK version, so install the
  SDKs for the entries you run; a missing SDK fails fast with the scanner's build error. The
  gate compares findings and exit codes, not build logs.
- The script builds the scanner from the working tree. It does not validate the packaged tool;
  use the package validation scripts for that.
- The full gate is not part of pull-request CI. It needs network access, package restores, and
  enough disk for the clones, so it runs locally before a release or after a precision change.

See the [false-positive policy](false-positive-policy.md) for the standard a new finding must
meet, and [Scanning a solution](scanning.md) for scanner flags and output formats.
