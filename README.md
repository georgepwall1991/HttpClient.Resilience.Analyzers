# HttpClient.Resilience.Analyzers

[![CI](https://github.com/georg-jung/HttpClient.Resilience.Analyzers/actions/workflows/ci.yml/badge.svg)](https://github.com/georg-jung/HttpClient.Resilience.Analyzers/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/HttpClient.Resilience.Analyzers.svg)](https://www.nuget.org/packages/HttpClient.Resilience.Analyzers)

Compile-time safety for `.NET` `HttpClient`, `IHttpClientFactory`, typed clients, retries, handlers, and outbound HTTP resilience.

## What It Catches

- `HttpClient` created and disposed per request.
- Static or singleton-owned manual `HttpClient` instances without connection lifetime configuration.
- Factory-created clients cached in singleton services.
- Typed clients injected into singleton services.
- Duplicate typed client registrations.
- `DelegatingHandler` implementations that capture scoped request data.
- Duplicate resilience handlers.
- Unsafe HTTP methods retried without explicit idempotency configuration.
- `ResponseHeadersRead` responses that are not disposed.
- Obvious unbounded outbound HTTP fan-out.

## Install

```bash
dotnet add package HttpClient.Resilience.Analyzers
```

## Current Scaffold

The repository is scaffolded with:

- A `netstandard2.0` Roslyn analyzer assembly.
- A NuGet package project that packs analyzer DLLs under `analyzers/dotnet/cs`.
- An xUnit test project with analyzer test infrastructure.
- A sample project wired to the local analyzer project. The sample promotes HCR080 to warning locally so its build output visibly demonstrates every MVP diagnostic.
- Rule docs, `.editorconfig` profiles, and a GitHub Actions CI pipeline.

See [implementation status](docs/implementation-status.md) for current analyzer and code-fix coverage.

Implemented diagnostic slices:

- `HCR001` for high-confidence `new HttpClient()` usage in request-path types, loops, `using` ownership patterns, and top-level loop/using statements, with obvious test contexts skipped and a partial code fix when an `IHttpClientFactory` method, local-function, or primary-constructor parameter is already in scope.
- `HCR002` for static or singleton-owned manual `HttpClient` fields without `PooledConnectionLifetime`, with configured-handler and namespace-aware qualified-registration recognition plus a safe code fix for parameterless client initializers.
- `HCR003` for `IHttpClientFactory` clients cached through assignments or initializers into static fields or known singleton fields, including receiver validation and namespace-aware qualified singleton registrations.
- `HCR004` for typed clients injected into singleton services, including namespace-aware qualified registration matching.
- `HCR005` for duplicate typed-client service registrations, including namespace-aware qualified registration matching, with a code fix.
- `HCR020` for request-scoped data and known scoped services captured by `DelegatingHandler` constructors, including namespace-aware qualified scoped service names.
- `HCR040` for duplicate `AddStandardResilienceHandler()` calls or same-name custom resilience handlers in one fluent `AddHttpClient`/`IHttpClientBuilder` chain, with namespace-aware lookalike-builder filtering and a code fix.
- `HCR041` for standard resilience handlers paired with visible unsafe typed-client or named-client calls across the compilation, including service-collection chain validation, namespace-aware qualified typed-client names, typed-client `HttpClient` receiver validation, unsafe `HttpRequestMessage` `Send`/`SendAsync` shapes, retry-guard detection, and a code fix.
- `HCR060` for undisposed awaited `ResponseHeadersRead` HTTP responses, with task-local filtering, returned-owner transfer heuristics, and a simple code fix.
- `HCR080` for obvious unbounded `Task.WhenAll` HTTP fan-out, with BCL `Task` and `HttpClient` receiver validation plus bounded-concurrency and connection-limit exclusions.

## Example

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

`HCR041` warns because the standard resilience pipeline can retry unsafe HTTP methods such as `POST`, `PUT`, `PATCH`, and `DELETE` unless configured otherwise.

```csharp
services.AddHttpClient<PaymentsClient>()
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.DisableForUnsafeHttpMethods();
    });
```

## Adoption Profiles

The package includes `.editorconfig` profiles under `profiles/`:

- `default.editorconfig` keeps the production-safety rules at their intended defaults.
- `brownfield-adoption.editorconfig` lowers most rules while teams triage an existing codebase.
- `strict-ci.editorconfig` promotes MVP warnings to errors for CI gates.
- `library-author.editorconfig` is stricter about streaming response ownership.

See [adoption](docs/adoption.md), [configuration](docs/configuration.md), and the [false-positive policy](docs/false-positive-policy.md) for rollout guidance.
