<p align="center">
  <img src="https://raw.githubusercontent.com/georgepwall1991/HttpClient.Resilience.Analyzers/main/assets/logo.png" alt="HttpClient.Resilience.Analyzers logo — Roslyn analyzers for HttpClient and IHttpClientFactory" width="160">
</p>

# HttpClient Resilience Analyzers — IHttpClientFactory, Polly, and Http.Resilience

Compile-time Roslyn analyzers and code fixes for .NET `HttpClient`, `IHttpClientFactory`, `AddHttpClient` typed clients, Polly, and `Microsoft.Extensions.Http.Resilience`.

Catch outbound HTTP reliability bugs at **build time**—socket exhaustion, missing `PooledConnectionLifetime`, DI lifetime leaks, stacked resilience handlers, unsafe POST retries, undisposed responses, and dropped cancellation tokens—before production, not at runtime.

> Analyzer-only package: no runtime dependency is added to your application.

## The problem

`HttpClient` misuse often compiles cleanly and looks fine in review. Socket exhaustion, stale DNS, double retries on POST, typed clients held by singletons, and undisposed `ResponseHeadersRead` streams show up under load—after deploy.

## What it catches

- Per-request `new HttpClient()` and long-lived clients without `PooledConnectionLifetime`
- Cached `IHttpClientFactory.CreateClient()` results and typed clients injected into singletons
- Duplicate typed-client registrations and shared implicit client names
- Stacked `AddStandardResilienceHandler` pipelines and unsafe-method retries
- Undisposed responses/streams, sync-over-async, missing `CancellationToken`
- Unbounded HTTP fan-out and fragile named-client string literals

The package currently ships **19** documented diagnostics (`HCR001`–`HCR085`), with automatic code fixes for common lifetime, retry, disposal, cancellation, and registration problems.

## Install

```bash
dotnet add package HttpClient.Resilience.Analyzers
```

For a library or shared project, keep the analyzer private to the project:

```xml
<PackageReference Include="HttpClient.Resilience.Analyzers" Version="0.1.143" PrivateAssets="all" />
```

Build normally with `dotnet build`. Diagnostics appear in supported IDEs, command-line builds, and CI without application configuration.

## See it work

Product-flow diagrams from the real showcase sample (not stock screenshots):

![HttpClient analyzer build diagnostics HCR001 HCR002 HCR041 for IHttpClientFactory and resilience](https://raw.githubusercontent.com/georgepwall1991/HttpClient.Resilience.Analyzers/main/assets/flow-ide-diagnostics.svg)

![Before and after code fix for HCR041 unsafe POST retries with AddStandardResilienceHandler](https://raw.githubusercontent.com/georgepwall1991/HttpClient.Resilience.Analyzers/main/assets/flow-before-after-fix.svg)

![HttpClient.Resilience.Analyzers product loop from source code to IDE and CI profiles](https://raw.githubusercontent.com/georgepwall1991/HttpClient.Resilience.Analyzers/main/assets/flow-product-loop.svg)

## 30-second path

1. Add the package reference with `PrivateAssets="all"`.
2. Run `dotnet build` — no service registration or runtime setup required.
3. Fix or suppress high-confidence findings on critical outbound paths.
4. Optionally copy a severity profile from the package `contentFiles` (`default`, `brownfield-adoption`, `strict-ci`, `library-author`).

## Feature snapshot

| Area | Examples |
|---|---|
| `HttpClient` lifetime | Per-request client creation, stale long-lived connections, cached factory clients |
| Dependency injection | Typed clients held by singletons, duplicate registrations, scoped state in handlers |
| Resilience and Polly | Duplicate handlers, unsafe HTTP method retries, per-request pipeline construction |
| Response ownership | Undisposed `ResponseHeadersRead` responses and HTTP content streams |
| Request correctness | Unchecked failure responses, shared default-header mutation, dropped cancellation tokens |
| Async and concurrency | Sync-over-async and obvious unbounded HTTP fan-out |
| Typed and named clients | Relative URLs without `BaseAddress`, duplicated string names, implicit-name collisions |

## Configure severity

```ini
[*.cs]
dotnet_diagnostic.HCR041.severity = error
dotnet_diagnostic.HCR080.severity = suggestion
```

## Compatibility

Targets Roslyn via a `netstandard2.0` analyzer assembly. Works with modern .NET SDK builds, ASP.NET Core, and any project that uses `HttpClient` / `IHttpClientFactory` patterns the rules can prove statically. Heuristic checks use lower default severity; deliberate exceptions can be suppressed per rule.

## Documentation

- [Complete rule catalog](https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers/blob/main/docs/rules/README.md)
- [Configuration guide](https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers/blob/main/docs/configuration.md)
- [Adoption guide](https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers/blob/main/docs/adoption.md)
- [Implementation status and limitations](https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers/blob/main/docs/implementation-status.md)
- [Analyzer health audit and 30-iteration backlog](https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers/blob/main/docs/analyzer-health.md)
- [Source and releases](https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers)

Licensed under the [MIT License](https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers/blob/main/LICENSE).
