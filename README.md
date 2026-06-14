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
- A sample project wired to the local analyzer project, with script-enforced build output coverage for every MVP diagnostic.
- Rule docs, `.editorconfig` profiles, and GitHub Actions CI/release pipelines.

See [implementation status](docs/implementation-status.md) for current analyzer and code-fix coverage.
See [releasing](docs/releasing.md) for the guarded NuGet publish workflow.

Implemented diagnostic slices:

- `HCR001` for high-confidence `new HttpClient()` usage in request-path types, Minimal API endpoint lambdas, loops, `using` ownership patterns, and top-level loop/using statements, with resolved custom `HttpClient` types and obvious test contexts skipped plus a partial code fix when an `IHttpClientFactory` method, local-function, or primary-constructor parameter is already in scope.
- `HCR002` for static or singleton-owned manual `HttpClient` field initializers and assignments without `PooledConnectionLifetime`, with resolved custom client types skipped, configured-handler and namespace-aware qualified-registration recognition, plus a safe code fix for parameterless client initializers.
- `HCR003` for `IHttpClientFactory` clients cached through assignments or initializers into static fields or known singleton fields, including namespace-aware factory receiver validation and qualified singleton registrations.
- `HCR004` for typed clients injected into singleton services, including nullable constructor parameters, visible `IServiceCollection` receiver validation, and namespace-aware qualified registration matching.
- `HCR005` for duplicate typed-client service registrations, including visible `IServiceCollection` receiver validation and namespace-aware qualified registration matching, with a code fix.
- `HCR020` for request-scoped data and known scoped services captured by direct or visibly inherited `DelegatingHandler` constructors, including visible `IServiceCollection` receiver validation, namespace-aware qualified scoped service names, and lookalike qualified handler-base filtering.
- `HCR040` for duplicate `AddStandardResilienceHandler()` calls or same-name custom resilience handlers with literal or constant names in one fluent `AddHttpClient`/`IHttpClientBuilder` chain, with namespace-aware lookalike-builder filtering and a code fix.
- `HCR041` for standard resilience handlers paired with visible unsafe typed-client or named-client calls across the compilation, including service-collection chain validation, namespace-aware qualified typed-client names, constant named-client names, namespace-aware typed-client `HttpClient` and named-client factory receiver validation, unsafe `HttpRequestMessage` `Send`/`SendAsync` shapes with literal or constant custom methods, retry-guard detection, and a code fix.
- `HCR060` for undisposed awaited `ResponseHeadersRead` HTTP responses, with resolved `HttpClient` receiver validation, task-local filtering, returned-owner constructor or initializer transfer heuristics, and a simple code fix.
- `HCR080` for obvious unbounded `Task.WhenAll` HTTP fan-out, with BCL `Task` and resolved `HttpClient` receiver validation plus bounded-concurrency, custom-client, and local/member connection-limit exclusions including shared handler fields.

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
