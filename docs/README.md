# HttpClient.Resilience.Analyzers Documentation

Documentation for `HttpClient.Resilience.Analyzers`, the .NET Roslyn analyzer package for `HttpClient`, `IHttpClientFactory`, typed clients, Polly, and outbound HTTP resilience.

New to the package? Follow the root [quick start](../README.md#quick-start), then use this hub to configure adoption and investigate individual diagnostics.

## Start Here

| Page | What it covers |
|---|---|
| [Rules index](rules/README.md) | All analyzer rules by category, default profile severity, fix support, and rollout priority. |
| [Configuration](configuration.md) | `.editorconfig` severity settings and included profile guidance. |
| [Adoption](adoption.md) | Practical rollout steps for brownfield and new .NET services. |
| [False-positive policy](false-positive-policy.md) | The diagnostic quality bar and how to report noisy analyzer behavior. |
| [Implementation status](implementation-status.md) | Current analyzer and code-fix coverage for every rule ID. |
| [Releasing](releasing.md) | Maintainer workflow for package validation and NuGet publishing. |

## Common Tasks

- **Install and see the first diagnostic:** [Quick start](../README.md#quick-start)
- **Understand a warning:** Search the [rules index](rules/README.md) by its `HCR` ID.
- **Change severity:** Use the [configuration guide](configuration.md).
- **Roll out to an existing service:** Start with the [adoption guide](adoption.md).
- **Report analyzer noise:** Follow the [false-positive policy](false-positive-policy.md).

## Rule Pages

Each rule page includes the production risk, bad and better C# examples, detection scope, code-fix notes, suppression guidance, and references.

- [Lifetime rules](rules/README.md#lifetime): `HCR001`, `HCR002`, `HCR003`
- [Typed-client and named-client rules](rules/README.md#typed-clients-and-named-clients): `HCR004`, `HCR005`, `HCR083`, `HCR084`
- [Handler rules](rules/README.md#handlers): `HCR020`
- [Resilience rules](rules/README.md#resilience): `HCR040`, `HCR041`, `HCR082`
- [Response lifetime and request correctness rules](rules/README.md#response-lifetime-and-request-correctness): `HCR060`, `HCR061`, `HCR062`, `HCR063`, `HCR064`, `HCR081`
- [Concurrency rules](rules/README.md#concurrency): `HCR080`
