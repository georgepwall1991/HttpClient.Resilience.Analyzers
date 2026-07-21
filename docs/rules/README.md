# HttpClient.Resilience.Analyzers Rules

This page is the rule catalog for `HttpClient.Resilience.Analyzers`, a Roslyn analyzer package for .NET `HttpClient`, `IHttpClientFactory`, typed clients, Polly, and `Microsoft.Extensions.Http.Resilience`.

Each rule page includes:

- why the pattern is risky;
- bad and better C# examples;
- current detection scope;
- code-fix behavior when available;
- suppression guidance;
- references to relevant .NET, Polly, or Roslyn documentation.

## Rules by Category

### Lifetime

| Rule | Title | Default profile | Fix support |
|---|---|---:|---|
| [`HCR001`](HCR001.md) | Do not create and dispose `HttpClient` per request | Warning | Partial |
| [`HCR002`](HCR002.md) | Long-lived manual `HttpClient` should configure `PooledConnectionLifetime` | Warning | Yes |
| [`HCR003`](HCR003.md) | Do not cache `IHttpClientFactory.CreateClient()` results long-term | Warning | Guide |

### Typed Clients and Named Clients

| Rule | Title | Default profile | Fix support |
|---|---|---:|---|
| [`HCR004`](HCR004.md) | Do not inject typed `HttpClient` clients into singleton services | Warning | Guide |
| [`HCR005`](HCR005.md) | Do not separately register a typed client already registered by `AddHttpClient<T>()` | Warning | Yes |
| [`HCR083`](HCR083.md) | Configure `BaseAddress` for typed clients that use relative URLs | Warning | Guide |
| [`HCR084`](HCR084.md) | Avoid duplicated string literals for named `HttpClient` names | Warning | Guide |

### Handlers

| Rule | Title | Default profile | Fix support |
|---|---|---:|---|
| [`HCR020`](HCR020.md) | `DelegatingHandler` should not capture scoped request data | Warning | Guide |

### Resilience

| Rule | Title | Default profile | Fix support |
|---|---|---:|---|
| [`HCR040`](HCR040.md) | Do not stack duplicate resilience handlers | Warning | Yes |
| [`HCR041`](HCR041.md) | Unsafe HTTP methods should not be retried unless explicitly configured | Warning | Yes |
| [`HCR082`](HCR082.md) | Avoid per-request creation of resilience pipelines | Warning | Guide |

### Response Lifetime and Request Correctness

| Rule | Title | Default profile | Fix support |
|---|---|---:|---|
| [`HCR060`](HCR060.md) | Dispose `HttpResponseMessage` when using `ResponseHeadersRead` | Warning | Yes |
| [`HCR061`](HCR061.md) | Check HTTP response success before reading content | Warning | Partial |
| [`HCR062`](HCR062.md) | Prefer per-request headers over mutating `DefaultRequestHeaders` | Warning | Guide |
| [`HCR063`](HCR063.md) | Avoid sync-over-async around outbound HTTP | Warning | Partial |
| [`HCR064`](HCR064.md) | Use cancellation-aware HTTP APIs when a token is available | Warning | Yes |
| [`HCR081`](HCR081.md) | Dispose streams returned from HTTP content | Warning | Partial |

### Concurrency

| Rule | Title | Default profile | Fix support |
|---|---|---:|---|
| [`HCR080`](HCR080.md) | High-concurrency HTTP fan-out should use bounded concurrency or connection limits | Suggestion | Guide |

## Adoption Priority

For brownfield services, start with the rules most likely to prevent production incidents:

| Priority | Rules | Why |
|---|---|---|
| 1 | `HCR041`, `HCR060`, `HCR061`, `HCR062`, `HCR063`, `HCR064`, `HCR081` | These catch unsafe retries, response ownership mistakes, shared header mutation, sync-over-async calls, and missing cancellation. |
| 2 | `HCR001`, `HCR002`, `HCR003`, `HCR004`, `HCR005`, `HCR020` | These catch lifetime and dependency-injection problems that are costly to debug after deployment. |
| 3 | `HCR040`, `HCR080`, `HCR082`, `HCR083`, `HCR084` | These improve resilience configuration, fan-out control, URI safety, and named-client maintainability. |

## Severity Profiles

The package ships with profiles under [`profiles/`](../../profiles):

| Profile | Purpose |
|---|---|
| [`default.editorconfig`](../../profiles/default.editorconfig) | Keeps production-safety diagnostics visible for new or actively maintained services. |
| [`brownfield-adoption.editorconfig`](../../profiles/brownfield-adoption.editorconfig) | Reduces noise while an existing codebase is being triaged. |
| [`strict-ci.editorconfig`](../../profiles/strict-ci.editorconfig) | Promotes production-safety warnings to CI-enforced errors. |
| [`library-author.editorconfig`](../../profiles/library-author.editorconfig) | Tightens response and stream ownership expectations for reusable libraries. |

Prefer changing individual `dotnet_diagnostic.<RULE_ID>.severity` values over disabling the whole package.
