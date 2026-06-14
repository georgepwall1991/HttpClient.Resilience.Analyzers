# Configuration

Rule severities are controlled through `.editorconfig` using `dotnet_diagnostic.<RULE_ID>.severity`.

```ini
[*.cs]
dotnet_diagnostic.HCR041.severity = warning
dotnet_diagnostic.HCR080.severity = suggestion
```

## Included Profiles

| Profile | Use when |
|---|---|
| `profiles/default.editorconfig` | New services or teams ready to act on all MVP warnings. |
| `profiles/brownfield-adoption.editorconfig` | Existing applications with an unknown warning baseline. |
| `profiles/strict-ci.editorconfig` | Repositories that want CI to fail on MVP warnings. |
| `profiles/library-author.editorconfig` | Libraries where response ownership mistakes should be treated more strictly. |

## Severity Guidance

Keep `HCR041` and `HCR060` visible during adoption. Unsafe retries and undisposed streaming responses can cause duplicated writes, connection pool pressure, and hard-to-debug production behavior.

Keep `HCR080` at `suggestion` or `warning` until the team agrees on fan-out limits. It is intentionally heuristic.

## Per-Rule Overrides

Rule pages under `docs/rules/` describe current detection and suppression guidance. Prefer changing individual severities over disabling the whole package.

## Analyzer Packaging References

- [NuGet analyzer package conventions](https://learn.microsoft.com/en-us/nuget/guides/analyzers-conventions)
- [Code analysis in .NET](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview)
- [Roslyn analyzer and code fix tutorial](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix)
