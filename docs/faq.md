---
title: HttpClient analyzer FAQ
description: Does HttpClient.Resilience.Analyzers replace Polly or Microsoft.Extensions.Http.Resilience? How it differs from CA2000, and how to configure one rule.
---

# FAQ

## Does this replace Microsoft.Extensions.Http.Resilience or Polly?

No. It statically checks how your code uses `HttpClient`, `IHttpClientFactory`, Polly, and the .NET resilience libraries. It does not add or replace a runtime resilience pipeline.

Use `AddStandardResilienceHandler` / `AddResilienceHandler` / Polly for retries, timeouts, and hedging at runtime. Use this analyzer to catch unsafe retries (including custom pipelines), stacked handlers, and lifetime bugs those libraries cannot see at compile time.

## Does the analyzer change my application at runtime?

No. The analyzer assembly runs at compile time and the package contains no application runtime dependency.

## How is this different from CA2000 or other IDisposable analyzers?

`CA2000` and similar rules talk about disposing objects. They do not understand `IHttpClientFactory`, typed-client DI lifetimes, `PooledConnectionLifetime`, `AddStandardResilienceHandler` or custom `AddResilienceHandler` pipelines retrying `POST`, or `ResponseHeadersRead` ownership. This package is a production-safety analyzer for outbound HTTP, not a general disposal or style analyzer.

## Can I enable, disable, or promote one rule?

Yes. Configure `dotnet_diagnostic.HCRxxx.severity` in `.editorconfig`. Each [rule page](rules/README.md) also explains when a narrow suppression may be appropriate.

```ini
[*.cs]
dotnet_diagnostic.HCR041.severity = error
dotnet_diagnostic.HCR080.severity = suggestion
```

## What should I do if a diagnostic is noisy or incorrect?

Check the rule's documented detection scope, reduce its severity if needed, and open an issue with a minimal reproduction. The project's [false-positive policy](false-positive-policy.md) treats diagnostic trust as a release requirement.

## Will this flood a brownfield codebase?

Not if you start with the [brownfield profile](adoption.md). High-confidence retry, hedging, and response-ownership rules stay visible; noisier lifetime checks can wait until the baseline is understood.

## Which IDEs show the warnings?

Any host that runs Roslyn analyzers during `dotnet build`: Visual Studio, Rider, C# Dev Kit / VS Code, and CI. Code fixes appear in supported IDEs.
