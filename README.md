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

- `HCR001` for high-confidence `new HttpClient()` usage in request-path types, Minimal API endpoint and route-group lambdas, loops, `using` ownership patterns, and top-level loop/using statements, with resolved custom `HttpClient` types and obvious xUnit/NUnit/MSTest contexts skipped plus a partial code fix when an `IHttpClientFactory` method, local-function, or primary-constructor parameter is already in scope.
- `HCR002` for static or singleton-owned manual `HttpClient` field/property initializers, direct assignments, and simple unreassigned local handoffs without `PooledConnectionLifetime`, with resolved custom client types skipped, configured handler local/field/property, namespace-aware qualified-registration recognition, visible singleton factory registrations that construct implementations, plus a safe code fix for parameterless field initializers.
- `HCR003` for `IHttpClientFactory` clients cached through direct assignments, simple unreassigned local handoffs, or initializers into static fields/properties or known singleton fields/properties, including namespace-aware factory receiver validation, qualified singleton registrations, and visible singleton factory registrations that construct an implementation.
- `HCR004` for typed clients injected into singleton services, including nullable and common wrapped constructor parameters, singleton factory service-provider resolutions, visible singleton factory registrations that construct an implementation, visible `IServiceCollection` receiver validation, `typeof(...)` singleton factory registrations, and namespace-aware qualified registration matching.
- `HCR005` for duplicate typed-client service registrations, including visible `IServiceCollection` receiver validation, namespace-aware qualified registration matching, `typeof(...)` standalone registrations, visible factory registrations that construct the typed-client implementation, and a code fix.
- `HCR020` for request-scoped data and known scoped services captured by direct or visibly inherited `DelegatingHandler` constructors, fields, or properties, including visible `IServiceCollection` receiver validation, scoped factory registrations that construct implementations, common deferred/collection wrappers, namespace-aware qualified request/scoped service names, and lookalike qualified handler-base filtering.
- `HCR040` for duplicate `AddStandardResilienceHandler()` calls or same-name custom resilience handlers with literal or constant names in one fluent `AddHttpClient`/`IHttpClientBuilder` chain, plus repeated standard handlers on the same visible builder local, with namespace-aware lookalike-builder filtering and a code fix.
- `HCR041` for standard resilience handlers paired with visible unsafe typed-client or named-client calls across the compilation, including service-collection chain validation, split `IHttpClientBuilder` locals, namespace-aware qualified typed-client names, one- or two-generic typed-client registrations, constant named-client names, namespace-aware typed-client `HttpClient` and named-client factory receiver validation including `this.`-qualified fields/properties, unsafe `HttpRequestMessage` `Send`/`SendAsync` shapes with literal or constant custom methods, retry-guard detection, and a code fix.
- `HCR060` for undisposed awaited `ResponseHeadersRead` HTTP responses, with resolved `HttpClient` receiver validation, `using`, direct block-level `Dispose()`, and `finally` ownership recognition, conditional-dispose filtering, task-local filtering, returned-owner constructor or initializer transfer heuristics, and a simple code fix.
- `HCR080` for obvious unbounded `Task.WhenAll` HTTP fan-out, with BCL `Task` and resolved `HttpClient` receiver validation plus same-receiver semaphore gating, custom-client, and local/member connection-limit exclusions including `this.`-qualified members and shared handler fields.

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
