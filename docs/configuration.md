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
| `profiles/default.editorconfig` | New services or teams ready to act on all configured warnings. |
| `profiles/brownfield-adoption.editorconfig` | Existing applications with an unknown warning baseline. |
| `profiles/strict-ci.editorconfig` | Repositories that want CI to fail on production-safety warnings. |
| `profiles/library-author.editorconfig` | Libraries where response ownership mistakes should be treated more strictly. |

## Severity Guidance

Keep `HCR041`, `HCR060`, `HCR061`, `HCR062`, `HCR063`, `HCR064`, `HCR081`, `HCR082`, `HCR083`, and `HCR084` visible during adoption. Unsafe retries, undisposed streaming responses, unchecked error responses, shared default header mutation, sync-over-async HTTP calls, dropped cancellation tokens, undisposed HTTP content streams, per-request resilience pipeline construction, typed-client relative URLs without a base address, and duplicated named-client literals can cause duplicated writes, connection pool pressure, misleading payload handling, cross-request data leakage, thread-pool starvation, runaway work after callers disconnect, leaked stream resources, avoidable allocation overhead, runtime URI failures, or name drift between registration and use sites.

Keep `HCR080` at `suggestion` or `warning` until the team agrees on fan-out limits. It is intentionally heuristic.

## Per-Rule Overrides

Rule pages under `docs/rules/` describe current detection and suppression guidance. Prefer changing individual severities over disabling the whole package.

## Analyzer Packaging References

- [NuGet analyzer package conventions](https://learn.microsoft.com/en-us/nuget/guides/analyzers-conventions)
- [Code analysis in .NET](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview)
- [Roslyn analyzer and code fix tutorial](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix)
