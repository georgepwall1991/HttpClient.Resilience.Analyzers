<p align="center">
  <img src="https://raw.githubusercontent.com/georgepwall1991/HttpClient.Resilience.Analyzers/main/assets/logo.png" alt="HttpClient.Resilience.Analyzers logo" width="180">
</p>

# HttpClient.Resilience.Analyzers

[![CI](https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers/actions/workflows/ci.yml/badge.svg)](https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/HttpClient.Resilience.Analyzers.svg)](https://www.nuget.org/packages/HttpClient.Resilience.Analyzers)
[![NuGet downloads](https://img.shields.io/nuget/dt/HttpClient.Resilience.Analyzers.svg)](https://www.nuget.org/packages/HttpClient.Resilience.Analyzers)
[![GitHub release](https://img.shields.io/github/v/release/georgepwall1991/HttpClient.Resilience.Analyzers)](https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Production-focused Roslyn analyzers and code fixes for .NET `HttpClient`, `IHttpClientFactory`, typed and named clients, Polly, and `Microsoft.Extensions.Http.Resilience`.

Catch outbound HTTP reliability bugs during development—not after deployment. The analyzer detects socket-exhaustion risks, stale DNS clients, DI lifetime leaks, unsafe retries, undisposed responses and streams, sync-over-async, dropped cancellation tokens, unbounded fan-out, and fragile named-client strings.

> The package is analyzer-only and adds no runtime dependency to your application.

## Quick Start

### 1. Install the analyzer

```bash
dotnet add package HttpClient.Resilience.Analyzers
```

Or add an explicit package reference:

```xml
<PackageReference Include="HttpClient.Resilience.Analyzers" Version="0.1.129" PrivateAssets="all" />
```

`PrivateAssets="all"` prevents the analyzer from flowing to projects that consume your project.

### 2. Build normally

```bash
dotnet build
```

Diagnostics appear in supported IDEs and in normal command-line or CI builds. No application code, service registration, or runtime configuration is required.

### 3. Review or fix the warning

For example, this typed client can send a non-idempotent `POST` through the standard retry pipeline:

```csharp
services.AddHttpClient<PaymentsClient>()
    .AddStandardResilienceHandler();

public sealed class PaymentsClient(HttpClient httpClient)
{
    public Task<HttpResponseMessage> CreateAsync(
        CancellationToken cancellationToken) =>
        httpClient.PostAsync("/payments", null, cancellationToken);
}
```

`HCR041` reports the risk. If unsafe methods are not deliberately idempotent, disable their retries:

```csharp
services.AddHttpClient<PaymentsClient>()
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.DisableForUnsafeHttpMethods();
    });
```

Several rules include automatic code fixes; every diagnostic links to a rule page explaining the risk, detection scope, safe alternatives, and suppression guidance.

## What It Protects

| Area | Examples of detected risk |
|---|---|
| Client lifetime | Per-request `HttpClient` creation, stale long-lived connections, cached factory clients |
| Dependency injection | Typed clients held by singletons, duplicate registrations, scoped state captured by handlers |
| Resilience and Polly | Duplicate handlers, unsafe-method retries, per-request pipeline construction |
| Response ownership | Undisposed `ResponseHeadersRead` responses and HTTP content streams |
| Request correctness | Unchecked failure responses, shared default-header mutation, missing cancellation |
| Async and concurrency | Sync-over-async and obvious unbounded HTTP fan-out |
| Typed and named clients | Relative URLs without `BaseAddress` and duplicated string client names |

The rules intentionally focus on concrete production risks. Heuristic checks use a lower default severity, and deliberate exceptions can be configured per rule.

## Rule Catalog

| Rule | Category | Detects | Default | Fix |
|---|---|---|---:|---|
| [`HCR001`](docs/rules/HCR001.md) | Lifetime | Creating and disposing `HttpClient` in request paths | Warning | Partial |
| [`HCR002`](docs/rules/HCR002.md) | Lifetime | Long-lived manual clients without `PooledConnectionLifetime` | Warning | Yes |
| [`HCR003`](docs/rules/HCR003.md) | Lifetime | Cached `IHttpClientFactory.CreateClient()` results | Warning | Guide |
| [`HCR004`](docs/rules/HCR004.md) | Typed clients | Typed clients injected into singleton services | Warning | Guide |
| [`HCR005`](docs/rules/HCR005.md) | Typed clients | Duplicate typed-client registrations | Warning | Yes |
| [`HCR020`](docs/rules/HCR020.md) | Handlers | `DelegatingHandler` capturing scoped request data | Warning | Guide |
| [`HCR040`](docs/rules/HCR040.md) | Resilience | Duplicate resilience handlers in one client pipeline | Warning | Yes |
| [`HCR041`](docs/rules/HCR041.md) | Resilience | Unsafe HTTP methods retried without explicit configuration | Warning | Yes |
| [`HCR060`](docs/rules/HCR060.md) | Response lifetime | Undisposed `ResponseHeadersRead` responses | Warning | Yes |
| [`HCR061`](docs/rules/HCR061.md) | Response lifetime | Response content read before checking success | Warning | Partial |
| [`HCR062`](docs/rules/HCR062.md) | Response lifetime | Per-request values written to `DefaultRequestHeaders` | Warning | Guide |
| [`HCR063`](docs/rules/HCR063.md) | Response lifetime | Sync-over-async around outbound HTTP | Warning | Partial |
| [`HCR064`](docs/rules/HCR064.md) | Response lifetime | HTTP calls that omit an available `CancellationToken` | Warning | Yes |
| [`HCR080`](docs/rules/HCR080.md) | Concurrency | Obvious unbounded `Task.WhenAll` HTTP fan-out | Suggestion | Guide |
| [`HCR081`](docs/rules/HCR081.md) | Response lifetime | Undisposed streams returned from HTTP content | Warning | Partial |
| [`HCR082`](docs/rules/HCR082.md) | Resilience | Per-request resilience pipeline construction | Warning | Guide |
| [`HCR083`](docs/rules/HCR083.md) | Typed clients | Relative URLs used without a configured `BaseAddress` | Warning | Guide |
| [`HCR084`](docs/rules/HCR084.md) | Named clients | Duplicated string literals for named-client names | Warning | Guide |

See the [rules index](docs/rules/README.md) for categories and recommended rollout order, or open an individual rule for exact detection details and limitations.

## Configuration

Set any rule's severity in your repository's `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.HCR041.severity = error
dotnet_diagnostic.HCR080.severity = suggestion
```

Supported values include `error`, `warning`, `suggestion`, `silent`, and `none`. Prefer a targeted override over disabling the analyzer package.

The repository includes ready-made starting profiles:

| Profile | Best for |
|---|---|
| [`default.editorconfig`](profiles/default.editorconfig) | New services and teams ready to act on production-safety warnings |
| [`brownfield-adoption.editorconfig`](profiles/brownfield-adoption.editorconfig) | Existing applications that need a lower-noise first pass |
| [`strict-ci.editorconfig`](profiles/strict-ci.editorconfig) | Clean repositories that want production-safety findings to fail CI |
| [`library-author.editorconfig`](profiles/library-author.editorconfig) | Libraries with stricter response and stream ownership requirements |

For one intentional exception, keep the reason next to the code:

```csharp
#pragma warning disable HCR041 // The endpoint enforces idempotency keys, so retries are safe.
services.AddHttpClient<PaymentsClient>()
    .AddStandardResilienceHandler();
#pragma warning restore HCR041
```

See [configuration](docs/configuration.md), [adoption](docs/adoption.md), and the [false-positive policy](docs/false-positive-policy.md) for team rollout guidance.

## Recommended Adoption

1. Install the package and build without changing CI gates.
2. For an established codebase, begin with the [brownfield profile](profiles/brownfield-adoption.editorconfig).
3. Fix high-confidence findings in critical outbound paths first.
4. Suppress deliberate exceptions narrowly and record why they are safe.
5. Move to the default profile, then strict CI, once the baseline is clean.

## FAQ

### Does this replace `Microsoft.Extensions.Http.Resilience` or Polly?

No. It statically checks how your code uses `HttpClient`, `IHttpClientFactory`, Polly, and the .NET resilience libraries. It does not add or replace a runtime resilience pipeline.

### Does the analyzer change my application at runtime?

No. The analyzer assembly runs at compile time and the package contains no application runtime dependency.

### Can I enable, disable, or promote one rule?

Yes. Configure `dotnet_diagnostic.HCRxxx.severity` in `.editorconfig`. Each rule page also explains when a narrow suppression may be appropriate.

### What should I do if a diagnostic is noisy or incorrect?

Check the rule's documented detection scope, reduce its severity if needed, and open an issue with a minimal reproduction. The project's [false-positive policy](docs/false-positive-policy.md) treats diagnostic trust as a release requirement.

## Documentation and Community

- [Documentation hub](docs/README.md)
- [All analyzer rules](docs/rules/README.md)
- [Implementation status and limitations](docs/implementation-status.md)
- [Contributing](CONTRIBUTING.md)
- [Code of Conduct](CODE_OF_CONDUCT.md)
- [Support](SUPPORT.md)
- [Security policy](SECURITY.md)
- [MIT license](LICENSE)

Contributions and focused bug reports are welcome. Please include the rule ID and a minimal C# reproduction when reporting an analyzer false positive or false negative.
