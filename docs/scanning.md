---
title: HCR Scan — scan any .NET solution for HttpClient resilience problems
description: Run the HttpClient.Resilience.Analyzers rules on a solution without changing its project files, review suspected false positives with source context, and emit SARIF, HTML, or Markdown.
---

# HCR Scan

`HttpClient.Resilience.Analyzers.Scan` runs the analyzer against a solution, project, or
directory without adding a package to it. Use it to try the rules on a real codebase, review a
suspected false positive with the surrounding source, compare releases, or gate a repository in CI.

The scanner:

- builds with the analyzers injected through MSBuild;
- reports the rule, location, severity, and line of code;
- writes SARIF 2.1.0 for GitHub code scanning;
- can write a self-contained HTML review page and a Markdown summary;
- supports rule, path, and baseline filters;
- leaves project files untouched unless you explicitly pass `--fix`.

## Run without installing

On the .NET 10 SDK:

```bash
dnx HttpClient.Resilience.Analyzers.Scan -- MyApp.slnx
dnx HttpClient.Resilience.Analyzers.Scan -- MyApp.slnx --html hcr-review.html
```

On the .NET 8 or 9 SDK, install the tool once:

```bash
dotnet tool install -g HttpClient.Resilience.Analyzers.Scan
hcr-scan MyApp.slnx
```

Put arguments after `--` when using `dnx`.

The scanner targets `net8.0` and rolls forward to newer major runtimes, so the tool itself runs
on .NET 8, 9, and 10. The scanned project keeps its own target framework and SDK.

## What it prints

```text
HCR Scan found 3 problems (2 rules, 2 files).

  Rule   Severity Count  Title
  HCR001 Warning      2  Do not create and dispose HttpClient per request
  HCR060 Warning      1  Dispose HttpResponseMessage when using ResponseHeadersRead

Findings:

  HCR001  Do not create and dispose HttpClient per request
    src/Payments/PaymentsClient.cs:24:30
      Do not create and dispose HttpClient per request
      24 | using var client = new HttpClient();
```

Every finding links to its rule page. The default SARIF path is
`httpclient-resilience.sarif`; change it with `-o` or `--sarif`.

## Review suspected false positives

The HTML report groups findings by rule and shows a few lines of source around every hit. It
includes severity toggles and search, so a reviewer can check the exact code without opening the
solution:

```bash
hcr-scan ./MyApp.slnx --findings all --html hcr-review.html
```

To stay focused, scan one rule:

```bash
hcr-scan ./MyApp.slnx --rules HCR060 --html hcr060-review.html
```

Useful filters:

| Option | What it does |
| --- | --- |
| `--rules HCR001,HCR060` | Report only these rules. Repeatable. |
| `--skip-rules HCR040` | Leave these rules out. Repeatable. |
| `--exclude '**/Migrations/**'` | Leave out matching paths. A glob without `/` matches a file or folder name. |
| `--findings all` | List every finding under its rule instead of the first three. |
| `--top 20` | List more files under “Most affected files”. |

Diagnostics suppressed with `#pragma` or `[SuppressMessage]` are omitted, just as they are in a
normal build. If a finding is still a false positive, check the rule page's detection scope and
open an issue with a minimal reproduction using the
[false-positive template](https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers/issues/new?template=false_positive.yml).

## Fail CI only on new findings

Use a previous SARIF report as a baseline while an existing codebase is being cleaned up:

```bash
hcr-scan MyApp.slnx \
  --baseline baseline.sarif \
  --sarif current.sarif \
  --fail-on warning
```

Only findings that are not in the baseline are listed and counted by `--fail-on`. The new SARIF
report marks each result as `new` or `unchanged`, so code scanning keeps the full picture.

The exit codes are:

| Code | Meaning |
| ---: | --- |
| `0` | Scan completed and no finding met `--fail-on`. |
| `1` | At least one finding met `--fail-on`. |
| `2` | Invalid arguments, a missing target, or an unreadable baseline. |
| `3` | The build or `dotnet format` failed, or nothing could be scanned. |

## GitHub Actions

Upload the SARIF report so findings appear in the repository's code scanning alerts:

```yaml
- name: Scan for HttpClient resilience problems
  run: dnx HttpClient.Resilience.Analyzers.Scan -- MyApp.slnx --sarif hcr.sarif --fail-on warning

- name: Upload SARIF
  if: always()
  uses: github/codeql-action/upload-sarif@v4
  with:
    sarif_file: hcr.sarif
```

When the scanner detects GitHub Actions, it also appends a Markdown summary to the job summary
and writes warning annotations for the most severe findings. Pass `--no-github` to turn that off,
or `--summary hcr-summary.md` to keep a copy in the workspace.

## Apply safe code fixes

Some rules include code fixes. `--fix` runs `dotnet format analyzers` with the same rule filters,
then scans what remains:

```bash
hcr-scan MyApp.slnx --fix --rules HCR041,HCR042,HCR043
```

`--fix` edits source files. Commit or stash first and review `git diff`; use `--exclude` to keep a
fix away from generated or legacy paths. Rules without an automatic fix are never changed.

## Build behavior and limitations

- The scanner runs `dotnet build` with `--no-incremental`, so every targeted compilation runs the
  analyzers, including projects that are already up to date.
- It injects its bundled analyzer assembly through `CustomAfterMicrosoftCommonTargets`. An
  existing copy of the analyzer package in the project is replaced for that build, so a scan does
  not report the same rule twice or mix tool versions.
- It disables the SDK's CA and IDE analyzers during the scan, and forces repository-wide
  `dotnet_analyzer_diagnostic.*` error settings back to each rule's default severity. This keeps a
  finding from stopping dependent projects before they are scanned. A repository's own
  `dotnet_diagnostic.HCRxxx.severity` setting still wins.
- Only `HCR` rules are reported. Compiler errors, warnings, and other analyzers in the same log are
  ignored.
- Findings are deduplicated across target frameworks by rule, file, line, and column.
- If a build fails, results from projects that compiled before the failure are still reported, and
  the scanner exits with code `3`. Run again with `--verbose` to see the full build output.
- The scanner builds the code it scans. Use it only on repositories you trust, with the same
  isolation you would give `dotnet build`.

## Options

| Option | Meaning |
| --- | --- |
| `<path>` | Solution (`.sln` or `.slnx`), project, or directory. Defaults to the current directory. |
| `-o`, `--sarif <file>` | SARIF 2.1.0 report path. Defaults to `httpclient-resilience.sarif`. |
| `-c`, `--configuration <name>` | Build configuration passed to `dotnet build`. |
| `-f`, `--framework <tfm>` | Build one target framework only. |
| `--no-restore` | Skip the implicit restore. |
| `--rules <ids>` | Report only these rules, comma-separated. Repeatable. |
| `--skip-rules <ids>` | Leave these rules out, comma-separated. Repeatable. |
| `--exclude <glob>` | Leave out findings in matching files. Repeatable. |
| `--fail-on <level>` | Exit `1` for findings at least this severe: `error`, `warning`, or `info`. Default: no failure. |
| `--baseline <file>` | Earlier SARIF report; only new findings are listed and count for `--fail-on`. |
| `--fix` | Apply code fixes with `dotnet format` before scanning. |
| `--html <file>` | Write a self-contained HTML review report. |
| `--summary <file>` | Write the report as Markdown. |
| `--no-github` | Do not write job summaries or annotations in GitHub Actions. |
| `--findings <n\|all>` | Findings listed under each rule. Defaults to 3. |
| `--top <n>` | Files listed under “Most affected files”. Defaults to 10. |
| `-v`, `--verbose` | Show the full `dotnet build` or `dotnet format` output. |
| `--version` | Show the scanner and bundled analyzer version. |
| `-h`, `--help` | Show usage. |

## Related documentation

- [Getting started with the analyzer package](getting-started.md)
- [Rule catalog](rules/README.md)
- [Configuration](configuration.md)
- [Adoption](adoption.md)
- [False-positive policy](false-positive-policy.md)
