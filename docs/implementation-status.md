# Implementation Status

This project currently implements every MVP diagnostic ID from the starter document.

## Implemented Rules

| Rule | Analyzer | Code fix | Notes |
|---|---:|---:|---|
| `HCR001` | Yes | Partial | High-confidence `new HttpClient()` detection in request-path types, Minimal API endpoint lambdas, loops, `using` ownership patterns, and top-level loop/using statements, with resolved custom `HttpClient` types and obvious test contexts skipped; code fix uses an existing method, local function, or primary-constructor `IHttpClientFactory` parameter when one is already in scope. |
| `HCR002` | Yes | Yes | Static or singleton-owned manual field/property client initializers and assignments without `PooledConnectionLifetime`, including resolved custom client filtering, recognition for configured handler fields, and namespace-aware qualified singleton registrations; code fix is limited to safe parameterless field initializers. |
| `HCR003` | Yes | No | Factory-created clients cached through assignments or initializers into static fields/properties or fields/properties on known singleton services across the compilation, with namespace-aware `IHttpClientFactory` receiver validation and qualified singleton registrations. |
| `HCR004` | Yes | Guide | Compilation-wide registration model for typed clients injected into singletons, including nullable constructor parameters, visible `IServiceCollection` receiver validation, and namespace-aware matching for visible qualified singleton and typed-client type names. |
| `HCR005` | Yes | Yes | Duplicate typed-client registrations across the compilation, including visible `IServiceCollection` receiver validation and namespace-aware matching for visible qualified type names. |
| `HCR020` | Yes | Guide | High-confidence request-scoped and known scoped service constructor dependencies in direct or visibly inherited handler implementations, including visible `IServiceCollection` receiver validation, namespace-aware qualified request/scoped service names, nullable scoped service names, plus lookalike qualified handler-base filtering. |
| `HCR040` | Yes | Yes | Duplicate standard resilience handlers and same-name custom resilience handlers with literal or constant names in one fluent `AddHttpClient`/`IHttpClientBuilder` chain, with namespace-aware lookalike custom builders skipped. |
| `HCR041` | Yes | Yes | Standard resilience handlers with visible unsafe typed-client or named-client calls across the compilation, including service-collection chain validation, namespace-aware qualified typed-client names, one- or two-generic typed-client registrations, constant named-client names, and unsafe `HttpRequestMessage` `Send`/`SendAsync` shapes with literal or constant custom methods; validates namespace-aware typed-client `HttpClient` and named-client factory receivers, and skips disabled retries and safe-method-only retry predicates. |
| `HCR060` | Yes | Yes | Awaited `ResponseHeadersRead` HTTP response ownership and disposal, with resolved `HttpClient` receiver validation, task-local filtering, and returned response/wrapper constructor or initializer transfer heuristics. |
| `HCR080` | Yes | Guide | Obvious unbounded `Task.WhenAll` HTTP fan-out with BCL `Task` and resolved `HttpClient` receiver validation; skips visible semaphore gates, bounded `Parallel.ForEachAsync`, local/member `MaxConnectionsPerServer` clients including shared handler fields, and resolved custom clients or lookalike async methods on non-HTTP clients. |

## Current Limitations

- Cross-file DI graph analysis is intentionally lightweight; it matches direct `IServiceCollection`-shaped registration calls across syntax trees but does not expand arbitrary custom wrapper semantics beyond visible calls.
- `HCR041` models visible typed-client and named-client call sites across syntax trees, including string literals and compile-time constants for named clients and custom `HttpMethod` string names, but it does not trace values through mutable variables or configuration.
- `HCR060` uses local ownership heuristics rather than full control-flow analysis.
- `HCR080` is intentionally suggestion-level and heuristic.
