# Launch Draft: Compile-Time Safety for .NET Outbound HTTP

Most .NET services use `HttpClient`, but many production issues come from patterns that compile cleanly and look harmless in review: per-request clients, stale long-lived clients, duplicated retries, handler scope leaks, undisposed streaming responses, and unbounded fan-out.

`HttpClient.Resilience.Analyzers` is a Roslyn analyzer package focused on those outbound HTTP failure modes.

## What It Catches

- Per-request `new HttpClient()` usage.
- Static or singleton-owned manual clients without `SocketsHttpHandler.PooledConnectionLifetime`.
- Factory-created clients cached in static fields or known singleton services.
- Typed clients injected into singleton services.
- Duplicate typed-client service registrations.
- `DelegatingHandler` constructors that capture request-scoped data.
- Duplicate standard or same-name custom resilience handlers.
- Unsafe HTTP methods retried by standard resilience handlers without explicit guardrails, including typed-client and named-client cases across the compilation.
- `ResponseHeadersRead` responses whose ownership is not disposed or transferred.
- Response content reads before visible success handling.
- Shared `DefaultRequestHeaders` mutations for per-request data.
- Sync-over-async around outbound HTTP calls.
- Missing cancellation-token flow into outbound HTTP APIs.
- Undisposed streams returned from HTTP content.
- Obvious unbounded `Task.WhenAll` outbound HTTP fan-out.
- Per-request resilience pipeline construction.
- Typed clients using relative URLs without a configured `BaseAddress`.

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

`HCR041` flags this because the standard resilience handler can retry unsafe HTTP methods. The safe default is to disable retries for unsafe methods unless the endpoint is explicitly idempotent.

```csharp
services.AddHttpClient<PaymentsClient>()
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.DisableForUnsafeHttpMethods();
    });
```

## Philosophy

The package is intentionally not a style analyzer. Warnings should feel like production incidents avoided. Rules start conservative, document their assumptions, and include suppression guidance for legitimate edge cases.

## Status

The initial preview implements the MVP diagnostics plus the first response-correctness expansion, with tests, documentation, sample cases, `.editorconfig` profiles, and NuGet analyzer packaging.
