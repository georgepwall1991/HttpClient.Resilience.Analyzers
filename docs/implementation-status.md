# Implementation Status

This project currently implements every MVP diagnostic ID from the starter document.

## Implemented Rules

| Rule | Analyzer | Code fix | Notes |
|---|---:|---:|---|
| `HCR001` | Yes | Partial | High-confidence `new HttpClient()` detection in request-path types, loops, `using` ownership patterns, and top-level loop/using statements, with obvious test contexts skipped; code fix uses an existing method, local function, or primary-constructor `IHttpClientFactory` parameter when one is already in scope. |
| `HCR002` | Yes | Yes | Static or singleton-owned manual client without `PooledConnectionLifetime`, including recognition for configured handler fields and namespace-aware qualified singleton registrations; code fix is limited to safe parameterless client initializers. |
| `HCR003` | Yes | No | Factory-created clients cached through assignments or initializers into static fields or fields on known singleton services across the compilation, with `IHttpClientFactory` receiver validation and namespace-aware qualified singleton registrations. |
| `HCR004` | Yes | Guide | Compilation-wide registration model for typed clients injected into singletons, including namespace-aware matching for visible qualified singleton and typed-client type names. |
| `HCR005` | Yes | Yes | Duplicate typed-client registrations across the compilation, including namespace-aware matching for visible qualified type names. |
| `HCR020` | Yes | Guide | High-confidence request-scoped and known scoped service constructor dependencies in handlers, including namespace-aware qualified and nullable scoped service names. |
| `HCR040` | Yes | Yes | Duplicate standard resilience handlers and same-name custom resilience handlers in one fluent `AddHttpClient`/`IHttpClientBuilder` chain, with lookalike custom builders skipped. |
| `HCR041` | Yes | Yes | Standard resilience handlers with visible unsafe typed-client or named-client calls across the compilation, including namespace-aware qualified typed-client names and unsafe `HttpRequestMessage` `Send`/`SendAsync` shapes; validates typed-client `HttpClient` receivers and skips disabled retries and safe-method-only retry predicates. |
| `HCR060` | Yes | Yes | Awaited `ResponseHeadersRead` HTTP response ownership and disposal, with `HttpClient` receiver validation, task-local filtering, and returned response/wrapper transfer heuristics. |
| `HCR080` | Yes | Guide | Obvious unbounded `Task.WhenAll` HTTP fan-out with BCL `Task` and `HttpClient` receiver validation; skips visible semaphore gates, bounded `Parallel.ForEachAsync`, local `MaxConnectionsPerServer` clients, and lookalike async methods on non-HTTP clients. |

## Current Limitations

- Cross-file DI graph analysis is intentionally lightweight; it matches direct `IServiceCollection`-shaped registration calls across syntax trees but does not expand arbitrary custom wrapper semantics beyond visible calls.
- `HCR041` models visible typed-client and named-client call sites across syntax trees, but it does not trace client names through variables or configuration.
- `HCR060` uses local ownership heuristics rather than full control-flow analysis.
- `HCR080` is intentionally suggestion-level and heuristic.
