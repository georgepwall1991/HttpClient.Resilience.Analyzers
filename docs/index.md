---
title: .NET HttpClient analyzer for IHttpClientFactory, Polly, and Http.Resilience
description: Compile-time Roslyn analyzers that catch HttpClient socket exhaustion, unsafe POST retries, typed-client leaks, and IHttpClientFactory mistakes before production.
---

# HttpClient.Resilience.Analyzers

Compile-time **.NET HttpClient analyzer** coverage for `HttpClient`, `IHttpClientFactory`, `AddHttpClient` typed and named clients, Polly, and `Microsoft.Extensions.Http.Resilience`.

Catch outbound HTTP reliability bugs during `dotnet build`—not after deploy. The package is analyzer-only and adds **no runtime dependency**.

[Install the package](getting-started.md){ .md-button .md-button--primary }
[Browse the rule catalog](rules/README.md){ .md-button }

```bash
dotnet add package HttpClient.Resilience.Analyzers
```

## The problem

Most .NET services use `HttpClient`, but many production incidents come from patterns that compile cleanly:

- Per-request `new HttpClient()` that exhausts sockets under load
- Long-lived clients without `PooledConnectionLifetime` that hold stale DNS
- Typed clients captured by singletons or registered twice
- `AddStandardResilienceHandler` retrying non-idempotent `POST`, or `AddStandardHedgingHandler` replaying it concurrently
- Undisposed streaming responses, sync-over-async, and dropped cancellation tokens

Those failures often appear only under traffic.

## What the analyzer catches

| Area | Examples |
|---|---|
| `HttpClient` lifetime | Per-request client creation, stale long-lived connections, cached factory clients |
| Dependency injection | Typed clients held by singletons, duplicate registrations, scoped state in handlers |
| Resilience and Polly | Duplicate handlers, unsafe-method retries, concurrent hedging of unsafe methods |
| Response ownership | Undisposed `ResponseHeadersRead` responses and HTTP content streams |
| Request correctness | Unchecked failure responses, shared default-header mutation, missing `CancellationToken` |
| Async and concurrency | Sync-over-async and obvious unbounded HTTP fan-out |
| Typed and named clients | Relative URLs without `BaseAddress`, duplicated string names, implicit-name collisions |

The rules intentionally focus on concrete production risks. Heuristic checks use a lower default severity.

## Who this is for

ASP.NET Core and .NET teams that already use `HttpClient`, `IHttpClientFactory`, typed clients, Polly, or `Microsoft.Extensions.Http.Resilience`. This package does not replace those libraries—it flags the production-safety mistakes they cannot see at compile time.

It is not a style analyzer, a formatter, or a runtime resilience pipeline.

## Hero diagnostic: unsafe POST retries

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

[HCR041](rules/HCR041.md) reports the retry risk. [HCR042](rules/HCR042.md) reports the same class of incident for `AddStandardHedgingHandler()`. Disable retries for unsafe methods unless the endpoint is deliberately idempotent:

```csharp
services.AddHttpClient<PaymentsClient>()
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.DisableForUnsafeHttpMethods();
    });
```

## Next steps

1. Follow the [getting started](getting-started.md) path, including solution-wide `Directory.Build.props` install.
2. Use the [brownfield adoption](adoption.md) profile on an existing service.
3. Open any `HCR` warning—the IDE “learn more” link goes to that rule’s page.
