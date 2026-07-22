<p align="center">
  <img src="https://raw.githubusercontent.com/georgepwall1991/HttpClient.Resilience.Analyzers/main/assets/logo.png" alt="HttpClient.Resilience.Analyzers logo" width="180">
</p>

# HttpClient.Resilience.Analyzers

[![CI](https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers/actions/workflows/ci.yml/badge.svg)](https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/HttpClient.Resilience.Analyzers.svg)](https://www.nuget.org/packages/HttpClient.Resilience.Analyzers)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Production-focused Roslyn analyzers for .NET `HttpClient`, `IHttpClientFactory`, typed clients, Polly, and `Microsoft.Extensions.Http.Resilience`.

`HttpClient.Resilience.Analyzers` catches outbound HTTP bugs at compile time: socket exhaustion risks, stale DNS clients, typed-client lifetime leaks, unsafe retries, response disposal mistakes, sync-over-async calls, missing cancellation tokens, unbounded fan-out, and fragile named-client strings.

## Install

```bash
dotnet add package HttpClient.Resilience.Analyzers
```

For explicit package references:

```xml
<PackageReference Include="HttpClient.Resilience.Analyzers" Version="0.1.51" PrivateAssets="all" />
```

The package is analyzer-only. It adds no runtime dependency to your application.

## Why This Exists

.NET gives teams several valid ways to use outbound HTTP: factory clients, typed clients, named clients, long-lived manual clients, resilience handlers, streaming responses, and custom handlers. The expensive failures usually come from small lifetime or ownership mistakes that are hard to spot in review.

This analyzer targets those failure modes directly:

- `HttpClient` lifetime mistakes that can cause socket exhaustion, DNS staleness, or connection churn.
- `IHttpClientFactory` and typed-client DI patterns that accidentally promote short-lived clients into singleton state.
- Resilience handler configuration that retries unsafe HTTP methods such as `POST`, `PUT`, `PATCH`, `DELETE`, or `CONNECT`.
- Response and stream ownership mistakes around `ResponseHeadersRead`, `HttpContent`, and streaming APIs.
- Request correctness issues such as missing cancellation tokens, shared `DefaultRequestHeaders`, and sync-over-async calls.
- Operational risk patterns such as unbounded outbound fan-out and per-request resilience pipeline construction.

## Quick Example

```csharp
services.AddHttpClient<PaymentsClient>()
    .AddStandardResilienceHandler();

public sealed class PaymentsClient(HttpClient httpClient)
{
    public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
    {
        return httpClient.PostAsync("/payments", null, cancellationToken);
    }
}
```

`HCR041` reports this because the standard resilience handler may retry unsafe HTTP methods. Retrying a non-idempotent `POST` can duplicate writes unless the endpoint is explicitly safe to retry.

```csharp
services.AddHttpClient<PaymentsClient>()
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.DisableForUnsafeHttpMethods();
    });
```

## Rule Catalog

The default profile keeps production-safety rules visible as warnings and leaves the heuristic fan-out rule as a suggestion. Every rule has a dedicated documentation page with bad code, better code, current detection details, suppression guidance, and references.

| Rule | Category | Catches | Default profile | Fix support |
|---|---|---|---:|---|
| [`HCR001`](docs/rules/HCR001.md) | Lifetime | Creating and disposing `HttpClient` in request paths | Warning | Partial |
| [`HCR002`](docs/rules/HCR002.md) | Lifetime | Long-lived manual `HttpClient` without `PooledConnectionLifetime` | Warning | Yes |
| [`HCR003`](docs/rules/HCR003.md) | Lifetime | Cached `IHttpClientFactory.CreateClient()` results | Warning | Guide |
| [`HCR004`](docs/rules/HCR004.md) | Typed clients | Typed clients injected into singleton services | Warning | Guide |
| [`HCR005`](docs/rules/HCR005.md) | Typed clients | Duplicate typed-client registrations | Warning | Yes |
| [`HCR020`](docs/rules/HCR020.md) | Handlers | `DelegatingHandler` capturing scoped request data | Warning | Guide |
| [`HCR040`](docs/rules/HCR040.md) | Resilience | Duplicate resilience handlers in a client pipeline | Warning | Yes |
| [`HCR041`](docs/rules/HCR041.md) | Resilience | Unsafe HTTP methods retried without explicit configuration | Warning | Yes |
| [`HCR060`](docs/rules/HCR060.md) | Response lifetime | Undisposed `ResponseHeadersRead` responses | Warning | Yes |
| [`HCR061`](docs/rules/HCR061.md) | Response lifetime | Reading response content before checking success | Warning | Partial |
| [`HCR062`](docs/rules/HCR062.md) | Response lifetime | Per-request headers written to `DefaultRequestHeaders` | Warning | Guide |
| [`HCR063`](docs/rules/HCR063.md) | Response lifetime | Sync-over-async around outbound HTTP | Warning | Partial |
| [`HCR064`](docs/rules/HCR064.md) | Response lifetime | HTTP calls that omit an available `CancellationToken` | Warning | Yes |
| [`HCR080`](docs/rules/HCR080.md) | Concurrency | Obvious unbounded `Task.WhenAll` HTTP fan-out | Suggestion | Guide |
| [`HCR081`](docs/rules/HCR081.md) | Response lifetime | Undisposed streams returned from HTTP content | Warning | Partial |
| [`HCR082`](docs/rules/HCR082.md) | Resilience | Per-request resilience pipeline construction | Warning | Guide |
| [`HCR083`](docs/rules/HCR083.md) | Typed clients | Typed clients using relative URLs without `BaseAddress` | Warning | Guide |
| [`HCR084`](docs/rules/HCR084.md) | Typed clients | Duplicated string literals for named `HttpClient` names | Warning | Guide |

See the full [rules index](docs/rules/README.md) for rollout priority, categories, and links.

## Adoption Profiles

Use the included `.editorconfig` profiles to match the analyzer to your team and codebase:

| Profile | Use it for |
|---|---|
| [`profiles/default.editorconfig`](profiles/default.editorconfig) | New services or teams ready to act on production-safety warnings. |
| [`profiles/brownfield-adoption.editorconfig`](profiles/brownfield-adoption.editorconfig) | Existing applications that need a low-noise first pass. |
| [`profiles/strict-ci.editorconfig`](profiles/strict-ci.editorconfig) | Repositories that want CI to fail on production-safety warnings. |
| [`profiles/library-author.editorconfig`](profiles/library-author.editorconfig) | Libraries where response and stream ownership should be stricter. |

Recommended rollout:

1. Add the package.
2. Start with the brownfield profile if the repository already has significant outbound HTTP code.
3. Fix or intentionally suppress high-confidence findings first.
4. Move to the default profile once new warnings are actionable.
5. Promote to strict CI only after the current baseline is clean.

More detail: [adoption guide](docs/adoption.md), [configuration guide](docs/configuration.md), and [false-positive policy](docs/false-positive-policy.md).

## Documentation

- [Documentation hub](docs/README.md)
- [Rules index](docs/rules/README.md)
- [Implementation status](docs/implementation-status.md)
- [Configuration](docs/configuration.md)
- [Adoption](docs/adoption.md)
- [False-positive policy](docs/false-positive-policy.md)
- [Releasing](docs/releasing.md)
- [Contributing](CONTRIBUTING.md)
- [Support](SUPPORT.md)
- [Security](SECURITY.md)

## Project Status

This is a preview package. The implemented analyzer set covers the MVP diagnostics and the first future-rule expansion through `HCR084`.

The quality bar is intentionally conservative: rules should report concrete outbound HTTP risks, avoid noisy guesses, and document the safe escape hatches when a project has a deliberate exception.
