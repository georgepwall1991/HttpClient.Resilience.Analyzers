<p align="center">
  <img src="https://raw.githubusercontent.com/georgepwall1991/HttpClient.Resilience.Analyzers/main/assets/logo.png" alt="HttpClient.Resilience.Analyzers logo" width="160">
</p>

# HttpClient Resilience Analyzers for .NET

Production-focused Roslyn analyzers and code fixes for `HttpClient`, `IHttpClientFactory`, typed and named clients, Polly, and `Microsoft.Extensions.Http.Resilience`.

Catch outbound HTTP reliability bugs at compile time: socket exhaustion, stale DNS connections, dependency-injection lifetime leaks, unsafe retries, missing cancellation tokens, undisposed responses and streams, sync-over-async, and unbounded fan-out.

> Analyzer-only package: no runtime dependency is added to your application.

## Install

```bash
dotnet add package HttpClient.Resilience.Analyzers
```

For a library or shared project, keep the analyzer private to the project:

```xml
<PackageReference Include="HttpClient.Resilience.Analyzers" Version="0.1.132" PrivateAssets="all" />
```

Build normally with `dotnet build`. Diagnostics appear in supported IDEs, command-line builds, and CI without application configuration.

## What It Detects

| Area | Examples |
|---|---|
| `HttpClient` lifetime | Per-request client creation, stale long-lived connections, cached factory clients |
| Dependency injection | Typed clients held by singletons, duplicate registrations, scoped state captured by handlers |
| Resilience and Polly | Duplicate handlers, unsafe HTTP method retries, per-request pipeline construction |
| Response ownership | Undisposed `ResponseHeadersRead` responses and HTTP content streams |
| Request correctness | Unchecked failure responses, shared default-header mutation, dropped cancellation tokens |
| Async and concurrency | Sync-over-async and obvious unbounded HTTP fan-out |
| Typed and named clients | Relative URLs without `BaseAddress` and duplicated string client names |

The package currently contains 18 documented diagnostics, including automatic fixes for common lifetime, retry, response-disposal, cancellation, and registration problems.

## Configure Severity

Every rule can be configured through `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.HCR041.severity = error
dotnet_diagnostic.HCR080.severity = suggestion
```

Ready-made profiles are included in the package and repository for default, brownfield, strict-CI, and library-author adoption.

## Documentation

- [Complete rule catalog](https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers/blob/main/docs/rules/README.md)
- [Configuration guide](https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers/blob/main/docs/configuration.md)
- [Adoption guide](https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers/blob/main/docs/adoption.md)
- [Implementation status and limitations](https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers/blob/main/docs/implementation-status.md)
- [Report a bug or false positive](https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers/issues/new/choose)
- [Source code and releases](https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers)

Licensed under the [MIT License](https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers/blob/main/LICENSE).
