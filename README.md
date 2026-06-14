# HttpClient.Resilience.Analyzers

Compile-time safety for `.NET` `HttpClient`, `IHttpClientFactory`, typed clients, retries, handlers, and outbound HTTP resilience.

## What It Catches

- `HttpClient` created and disposed per request.
- Static/manual `HttpClient` instances without connection lifetime configuration.
- Factory-created clients cached in singleton services.
- Typed clients injected into singleton services.
- Duplicate typed client registrations.
- `DelegatingHandler` implementations that capture scoped request data.
- Stacked resilience handlers.
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
- A sample project wired to the local analyzer project.
- Rule docs, `.editorconfig` profiles, and a GitHub Actions CI pipeline.

See [implementation status](docs/implementation-status.md) for current analyzer and code-fix coverage.

Implemented diagnostic slices:

- `HCR001` for high-confidence method-local `new HttpClient()` usage.
- `HCR002` for static manual `HttpClient` fields without `PooledConnectionLifetime`, with a code fix.
- `HCR003` for factory-created clients cached into static fields or known singleton fields.
- `HCR004` for typed clients injected into singleton services.
- `HCR005` for duplicate typed-client service registrations, with a code fix.
- `HCR020` for request-scoped data captured by `DelegatingHandler` constructors.
- `HCR040` for stacked `AddStandardResilienceHandler()` calls in one fluent chain, with a code fix.
- `HCR041` for standard resilience handlers paired with visible unsafe typed-client or named-client calls across the compilation, with a code fix.
- `HCR060` for undisposed `ResponseHeadersRead` responses, including a simple code fix.
- `HCR080` for obvious unbounded `Task.WhenAll` HTTP fan-out.

## Example

```csharp
var response = await client.SendAsync(
    request,
    HttpCompletionOption.ResponseHeadersRead,
    cancellationToken);
```

`HCR060` warns because a response opened with `ResponseHeadersRead` should be disposed by the caller that owns it.

```csharp
using var response = await client.SendAsync(
    request,
    HttpCompletionOption.ResponseHeadersRead,
    cancellationToken);
```
