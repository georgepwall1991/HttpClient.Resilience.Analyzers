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

- `HCR001` for high-confidence `new HttpClient()` usage in request-path types, Minimal API endpoint and route-group lambdas, loops, `using` ownership patterns, and top-level loop/using statements, with resolved custom `HttpClient` types, visible lookalike Minimal API receivers, and obvious xUnit/NUnit/MSTest contexts skipped plus a partial code fix when an `IHttpClientFactory` method, local-function, or primary-constructor parameter is already in scope.
- `HCR002` for static or singleton-owned manual `HttpClient` field/property initializers, direct assignments, and simple unreassigned local handoffs without `PooledConnectionLifetime`, with resolved custom client types skipped, reassignment-aware configured handler local/field/property recognition, namespace-aware qualified-registration recognition, visible singleton factory registrations that construct implementations, plus a safe code fix for parameterless field initializers.
- `HCR003` for `IHttpClientFactory` clients cached through direct assignments, simple unreassigned local handoffs, or initializers into static or known singleton `HttpClient` fields/properties, including namespace-aware and resolved member factory receiver validation, qualified singleton registrations, and visible singleton factory registrations that construct an implementation.
- `HCR004` for typed clients injected into singleton services, including nullable and common wrapped constructor parameters, namespace-aware constructor and factory resolution matching, lambda or anonymous singleton factory service-provider resolutions, visible singleton factory registrations that construct an implementation, visible `IServiceCollection` receiver validation, `typeof(...)` singleton factory registrations, and namespace-aware qualified registration matching.
- `HCR005` for duplicate typed-client service registrations, including visible `IServiceCollection` and minimal-hosting `Services` receiver validation, resolved namespace-aware registration matching, `typeof(...)` standalone registrations, visible factory registrations that construct the typed-client implementation, and a code fix.
- `HCR020` for request-scoped data and known scoped services captured by direct or visibly inherited `DelegatingHandler` constructors, fields, or properties, including visible `IServiceCollection` receiver validation, scoped factory registrations that construct implementations, common deferred/collection wrappers, namespace-aware qualified request/scoped service names, resolved custom request-type lookalike filtering, and resolved or qualified handler-base lookalike filtering.
- `HCR040` for duplicate `AddStandardResilienceHandler()` calls or same-name custom resilience handlers with literal or constant names in one fluent `AddHttpClient`/`IHttpClientBuilder` chain, plus repeated standard handlers on the same visible unreassigned builder receiver, with builder-return validation and namespace-aware lookalike-builder filtering plus a code fix.
- `HCR041` for standard resilience handlers paired with visible unsafe typed-client or named-client calls across the compilation, including service-collection and minimal-hosting `Services` chain validation, unreassigned split `IHttpClientBuilder` locals, resolved namespace-aware typed-client matching, one- or two-generic typed-client registrations, constant named-client names, namespace-aware typed-client `HttpClient` and named-client factory receiver validation including `this.`-qualified fields/properties, reassignment-aware named-client and request-message locals, unsafe `HttpRequestMessage` `Send`/`SendAsync` shapes with literal or constant custom methods, retry-guard detection, and a code fix.
- `HCR060` for undisposed awaited `ResponseHeadersRead` HTTP responses from local declarations or assignments, with resolved `HttpClient` receiver and response-return validation, `using`, reassignment-aware direct block-level `Dispose()`, `finally`, and same-block using-declaration ownership recognition, conditional-dispose filtering, task-local filtering, returned-owner constructor or initializer transfer heuristics, and a simple declaration code fix.
- `HCR080` for obvious unbounded `Task.WhenAll` HTTP fan-out, including inline or visible unreassigned local LINQ `Select(...)` task sequences, with BCL `Task` and resolved `HttpClient` receiver validation plus symbol-aware same-receiver `SemaphoreSlim` gating, custom-client, and reassignment-aware local/member real `SocketsHttpHandler` connection-limit exclusions including `this.`-qualified members and shared handler fields.

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

`HCR041` warns because the standard resilience pipeline can retry unsafe HTTP methods such as `POST`, `PUT`, `PATCH`, `DELETE`, and `CONNECT` unless configured otherwise.

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
