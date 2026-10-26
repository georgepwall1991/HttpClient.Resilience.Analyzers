# HCR Scan

Scan any .NET solution for HttpClient, `IHttpClientFactory`, typed-client, Polly, and
`Microsoft.Extensions.Http.Resilience` problems without adding a package to it.

```bash
dnx HttpClient.Resilience.Analyzers.Scan                 # .NET 10 SDK, no install step
dnx HttpClient.Resilience.Analyzers.Scan -- MyApp.slnx -o results.sarif

dotnet tool install -g HttpClient.Resilience.Analyzers.Scan   # .NET 8 or 9 SDK
hcr-scan MyApp.slnx
```

The scanner builds the solution with the `HttpClient.Resilience.Analyzers` analyzers injected, then
prints the rules that fired, how often, where, the line of code, and a link to each rule's page. It
also writes a SARIF 2.1.0 file for GitHub code scanning. Your project files are not changed unless
you pass `--fix`.

Use it to try the analyzers on a real codebase before adding the package, compare releases, and
review suspected false positives with the surrounding source in the HTML report.

## Options

| Option | Meaning |
| --- | --- |
| `<path>` | Solution (`.sln` or `.slnx`), project, or directory. Defaults to the current directory. |
| `-o`, `--sarif <file>` | Where to write the SARIF 2.1.0 report. Defaults to `httpclient-resilience.sarif`. |
| `-c`, `--configuration <name>` | Build configuration, passed to `dotnet build`. |
| `-f`, `--framework <tfm>` | Build one target framework only. |
| `--no-restore` | Skip the implicit restore. |
| `--rules <ids>` | Report only these rules, comma-separated, such as `HCR001,HCR060`. Repeatable. |
| `--skip-rules <ids>` | Leave these rules out, comma-separated. Repeatable. |
| `--exclude <glob>` | Leave out findings in matching files, such as `tests/**`. Repeatable. |
| `--fail-on <level>` | Exit with code 1 when a finding is at least this severe: `error`, `warning`, or `info`. Defaults to `none`. |
| `--baseline <file>` | A SARIF report from an earlier scan. Only findings it does not have are listed and count for `--fail-on`. |
| `--fix` | Apply the rules' code fixes with `dotnet format` first, then report what is left. |
| `--html <file>` | Also write a self-contained HTML report with code around each finding. |
| `--summary <file>` | Also write the report as Markdown. |
| `--no-github` | In GitHub Actions, skip the job summary and pull request annotations. |
| `--findings <n\|all>` | Findings to list under each rule. Defaults to 3. |
| `--top <n>` | Files to list under "Most affected files". Defaults to 10. |
| `-v`, `--verbose` | Show the full `dotnet build` output. |
| `--version` | Show the scanner version. |
| `-h`, `--help` | Show usage. |

Put the scanner's arguments after `--` when using `dnx`.

## False-positive triage

The HTML report is the fastest review surface:

```bash
hcr-scan ./MyApp.slnx --findings all --html hcr-review.html
```

It groups findings by rule, shows the message and a few lines of source around every hit, and
provides search and severity filters. For a focused review, limit the scan to one rule:

```bash
hcr-scan ./MyApp.slnx --rules HCR060 --html hcr060.html
```

The text and Markdown reports show the source line, so a suspected false positive can be checked
against the exact code path without opening the solution. Suppressed diagnostics are omitted, just
as they are in a normal build.

Full guide: https://georgepwall1991.github.io/HttpClient.Resilience.Analyzers/scanning/
