# HttpClient.Resilience.Analyzers

> Compile-time safety for `.NET` `HttpClient`, `IHttpClientFactory`, typed clients, handlers, retries, timeouts, and outbound HTTP resilience.

`HttpClient.Resilience.Analyzers` is a Roslyn analyzer package for catching fragile outbound HTTP code before it reaches production.

It focuses on the bugs that compile cleanly, pass code review, and then show up as socket exhaustion, stale DNS, duplicated writes, broken retries, handler scope leaks, undisposed responses, or brittle service-to-service calls.

## SEO-friendly package identity

**Package name:** `HttpClient.Resilience.Analyzers`  
**Repository name:** `HttpClient.Resilience.Analyzers`  
**Primary keyword:** `.NET HttpClient analyzer`  
**Secondary keywords:** `IHttpClientFactory analyzer`, `HttpClient resilience analyzer`, `Polly analyzer`, `Microsoft.Extensions.Http.Resilience analyzer`, `typed HttpClient analyzer`, `socket exhaustion analyzer`

Suggested NuGet description:

> Roslyn analyzers for .NET HttpClient, IHttpClientFactory, and Microsoft.Extensions.Http.Resilience. Catches socket exhaustion risks, DNS-stale clients, typed-client lifetime bugs, unsafe retries, handler scope leaks, response disposal mistakes, and fragile outbound HTTP patterns at compile time.

Suggested NuGet tags:

```text
httpclient;ihttpclientfactory;resilience;polly;dotnet;csharp;roslyn;analyzer;analyser;aspnetcore;static-analysis;socket-exhaustion;typed-client;retry;resilience-pipeline
```

## Why this should exist

Most `.NET` teams use `HttpClient`, but many production failures come from subtle patterns that are hard to spot manually:

- Creating and disposing `HttpClient` per request.
- Holding factory-created clients for too long.
- Injecting typed clients into singleton services.
- Retrying unsafe methods such as `POST`, `PUT`, `PATCH`, or `DELETE` without an explicit idempotency strategy.
- Capturing request-scoped data inside `DelegatingHandler` instances.
- Using `ResponseHeadersRead` without disposing the `HttpResponseMessage`.
- Creating high-concurrency outbound HTTP fan-out without connection limits or bounded concurrency.

This analyzer should not be a generic style package. It should be a high-signal production-safety analyzer for outbound HTTP.

## Positioning

### One-liner

> The production-safety analyzer for `.NET` outbound HTTP.

### Longer pitch

`HttpClient.Resilience.Analyzers` catches dangerous `HttpClient` and `IHttpClientFactory` patterns at compile time, including socket exhaustion risks, DNS staleness, typed-client lifetime bugs, unsafe retries, handler scope leaks, and response lifetime mistakes.

### What this is

- A Roslyn analyzer package for `.NET` services and libraries.
- Focused on outbound HTTP correctness and resilience.
- Opinionated, but conservative by default.
- Built for teams using `IHttpClientFactory`, typed clients, named clients, Polly, or `Microsoft.Extensions.Http.Resilience`.

### What this is not

- Not a formatting analyzer.
- Not a style analyzer.
- Not a generic performance analyzer.
- Not a replacement for runtime resilience policies.
- Not a package that fires 100 noisy warnings on day one.

## MVP rule set

Start with 10 rules. Make these excellent before adding more.

| Rule ID | Title | Default severity | Code fix | Notes |
|---|---|---:|---:|---|
| `HCR001` | Do not create and dispose `HttpClient` per request | Warning | Partial | Detect obvious `new HttpClient()` inside methods, loops, controllers, handlers, workers, and request paths. |
| `HCR002` | Long-lived manual `HttpClient` should configure `PooledConnectionLifetime` | Warning | Yes | Applies to static/singleton clients using default handler construction. |
| `HCR003` | Do not cache `IHttpClientFactory.CreateClient()` results long-term | Warning | Guide | Detect assignment of factory-created clients into singleton fields or static fields. |
| `HCR004` | Do not inject typed `HttpClient` clients into singleton services | Warning | Guide | Reuse lifetime graph logic from DI analyzer work. |
| `HCR005` | Do not separately register a typed client already registered by `AddHttpClient<T>()` | Warning | Yes | Avoids accidental duplicate or conflicting registrations. |
| `HCR020` | `DelegatingHandler` should not capture scoped request data | Warning | Guide | Detect `IHttpContextAccessor`, `HttpContext`, scoped services, user/session/request data in handlers. |
| `HCR040` | Do not stack multiple standard resilience handlers | Warning | Yes | Detect duplicate `AddStandardResilienceHandler()` / resilience handler stacking. |
| `HCR041` | Unsafe HTTP methods should not be retried unless explicitly configured | Warning | Yes | Hero rule. Warn on standard retry pipelines used with unsafe methods unless disabled or explicitly justified. |
| `HCR060` | Dispose `HttpResponseMessage` when using `ResponseHeadersRead` | Warning | Yes | Detect missing `using`, `await using`, `try/finally`, or ownership transfer. |
| `HCR080` | High-concurrency HTTP fan-out should use bounded concurrency or connection limits | Suggestion | Guide | Heuristic. Keep low severity until proven. |

## Hero diagnostic: `HCR041`

### Problem

The standard HTTP resilience pipeline can retry requests. Retrying unsafe HTTP methods such as `POST`, `PUT`, `PATCH`, or `DELETE` may duplicate side effects unless the operation is explicitly idempotent.

### Diagnostic message

> `HCR041: Standard resilience retries unsafe HTTP methods. Disable retries for POST/PUT/PATCH/DELETE unless the operation is idempotent.`

### Bad

```csharp
services.AddHttpClient<PaymentsClient>()
    .AddStandardResilienceHandler();
```

```csharp
public sealed class PaymentsClient(HttpClient httpClient)
{
    public Task<HttpResponseMessage> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        return httpClient.PostAsJsonAsync("/payments", request, cancellationToken);
    }
}
```

### Better

```csharp
services.AddHttpClient<PaymentsClient>()
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.DisableForUnsafeHttpMethods();
    });
```

### When to suppress

Suppress this rule only when at least one of the following is true:

- The endpoint is idempotent by design.
- The request uses idempotency keys.
- The retry predicate excludes unsafe methods.
- The API contract explicitly guarantees safe retries for the operation.

Suggested suppression comment:

```csharp
#pragma warning disable HCR041 // Endpoint uses idempotency keys and is safe to retry.
services.AddHttpClient<PaymentsClient>()
    .AddStandardResilienceHandler();
#pragma warning restore HCR041
```

## Rule design principles

1. **Prefer fewer, stronger diagnostics.** A warning should feel like a production incident avoided.
2. **Only warn when the analyzer has enough evidence.** Use `Info` or `Suggestion` for heuristics.
3. **Every rule needs docs.** Each rule page should explain the bug, the fix, and safe suppression cases.
4. **Every code fix must be boring and safe.** Do not rewrite architecture automatically.
5. **Make brownfield adoption painless.** Provide `.editorconfig` profiles for different strictness levels.

## Suggested repository structure

```text
HttpClient.Resilience.Analyzers/
  README.md
  LICENSE
  Directory.Build.props
  Directory.Packages.props
  global.json
  src/
    HttpClient.Resilience.Analyzers/
      Diagnostics/
        DiagnosticIds.cs
        DiagnosticCategories.cs
        DiagnosticDescriptors.cs
      Analyzers/
        Lifetime/
          HCR001_NewHttpClientInRequestPathAnalyzer.cs
          HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer.cs
          HCR003_CachedFactoryClientAnalyzer.cs
        TypedClients/
          HCR004_TypedClientInjectedIntoSingletonAnalyzer.cs
          HCR005_DuplicateTypedClientRegistrationAnalyzer.cs
        Handlers/
          HCR020_DelegatingHandlerCapturesScopedDataAnalyzer.cs
        Resilience/
          HCR040_StackedResilienceHandlersAnalyzer.cs
          HCR041_UnsafeMethodRetryAnalyzer.cs
        ResponseLifetime/
          HCR060_ResponseHeadersReadDisposalAnalyzer.cs
        Concurrency/
          HCR080_UnboundedHttpFanOutAnalyzer.cs
      CodeFixes/
        HCR002_AddPooledConnectionLifetimeCodeFixProvider.cs
        HCR005_RemoveDuplicateTypedClientRegistrationCodeFixProvider.cs
        HCR041_DisableUnsafeMethodRetriesCodeFixProvider.cs
        HCR060_DisposeResponseCodeFixProvider.cs
      KnownSymbols/
        HttpClientSymbols.cs
        DependencyInjectionSymbols.cs
        ResilienceSymbols.cs
        PollySymbols.cs
      Models/
        HttpClientRegistrationModel.cs
        ServiceRegistrationModel.cs
        OutboundHttpCallModel.cs
      Flow/
        HttpClientSourceTracker.cs
        ResponseDisposalTracker.cs
      HttpClientResilienceAnalyzersResources.resx
    HttpClient.Resilience.Analyzers.Package/
      HttpClient.Resilience.Analyzers.Package.csproj
  tests/
    HttpClient.Resilience.Analyzers.Tests/
      Lifetime/
      TypedClients/
      Handlers/
      Resilience/
      ResponseLifetime/
      Concurrency/
      TestInfrastructure/
  samples/
    HttpClient.Resilience.Showcase/
  docs/
    rules/
      HCR001.md
      HCR002.md
      HCR003.md
      HCR004.md
      HCR005.md
      HCR020.md
      HCR040.md
      HCR041.md
      HCR060.md
      HCR080.md
    adoption.md
    configuration.md
    false-positive-policy.md
  profiles/
    default.editorconfig
    strict-ci.editorconfig
    brownfield-adoption.editorconfig
    library-author.editorconfig
```

## Diagnostic IDs

Use `HCR` as the prefix:

```text
HCR = HttpClient Resilience
```

Reserved ranges:

| Range | Area |
|---:|---|
| `HCR001` - `HCR019` | `HttpClient` lifetime and factory usage |
| `HCR020` - `HCR039` | Handlers, DI scopes, and request data |
| `HCR040` - `HCR059` | Resilience, retries, timeouts, Polly, Microsoft.Extensions.Http.Resilience |
| `HCR060` - `HCR079` | Request/response correctness |
| `HCR080` - `HCR099` | High-load, concurrency, and fan-out |
| `HCR100`+ | Future rules |

## Initial implementation milestones

### Milestone 1: Package skeleton

- [ ] Create analyzer project targeting `netstandard2.0`.
- [ ] Create test project using `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing`.
- [ ] Add shared test helpers for verifying diagnostics and code fixes.
- [ ] Add `DiagnosticIds`, categories, and descriptor helpers.
- [ ] Add NuGet packaging metadata.
- [ ] Add CI build running tests and package validation.

### Milestone 2: Lifetime rules

- [ ] Implement `HCR001` for obvious method-local `new HttpClient()` usage.
- [ ] Implement `HCR002` for static/singleton `HttpClient` without `SocketsHttpHandler.PooledConnectionLifetime`.
- [ ] Implement `HCR003` for factory-created clients stored in static fields or singleton services.
- [ ] Add docs and examples for all three rules.

### Milestone 3: DI and typed clients

- [ ] Build a lightweight `IServiceCollection` registration model.
- [ ] Track `AddSingleton`, `AddScoped`, `AddTransient`, `AddHttpClient`, and typed client registrations.
- [ ] Implement `HCR004` for typed clients injected into singletons.
- [ ] Implement `HCR005` for duplicate typed client registration.
- [ ] Add docs, tests, and safe suppression guidance.

### Milestone 4: Resilience rules

- [ ] Track `AddStandardResilienceHandler` and custom resilience handler registrations.
- [ ] Implement `HCR040` for stacked standard resilience handlers.
- [ ] Implement `HCR041` for unsafe HTTP methods with retry-enabled clients.
- [ ] Add code fix for `DisableForUnsafeHttpMethods()`.
- [ ] Make `HCR041` the hero demo in the README.

### Milestone 5: Response lifetime

- [ ] Implement `HCR060` for `HttpCompletionOption.ResponseHeadersRead` without response disposal.
- [ ] Detect `using var`, `using (...)`, `try/finally`, and ownership transfer patterns.
- [ ] Add code fix for simple local variable cases.

### Milestone 6: Launch polish

- [ ] Add rule documentation pages.
- [ ] Add `brownfield-adoption.editorconfig`.
- [ ] Add sample project with intentionally bad examples.
- [ ] Add README badges.
- [ ] Publish `0.1.0-preview.1`.
- [ ] Create launch blog post.

## Implementation notes by rule

### `HCR001` - Do not create and dispose `HttpClient` per request

Detect:

```csharp
using var client = new HttpClient();
```

```csharp
using (var client = new HttpClient())
{
    // ...
}
```

```csharp
foreach (var item in items)
{
    var client = new HttpClient();
    await client.GetAsync(item.Url);
}
```

Start conservative:

- Warn when `new HttpClient()` appears inside a method body and the containing type looks like a controller, endpoint, worker, handler, service, or repository.
- Increase confidence if the client is disposed in the same method.
- Increase confidence if it appears inside a loop.
- Do not warn for obvious tests unless test-analysis mode is enabled.

### `HCR002` - Long-lived manual `HttpClient` should configure `PooledConnectionLifetime`

Detect:

```csharp
private static readonly HttpClient Client = new();
```

```csharp
private static readonly HttpClient Client = new HttpClient();
```

Suggest:

```csharp
private static readonly HttpClient Client = new(
    new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });
```

Avoid warning when:

- A `SocketsHttpHandler` is supplied with `PooledConnectionLifetime`.
- The client is clearly created by `IHttpClientFactory`.
- The code is in generated files.

### `HCR003` - Do not cache factory-created clients long-term

Detect:

```csharp
public sealed class MySingleton
{
    private readonly HttpClient _client;

    public MySingleton(IHttpClientFactory factory)
    {
        _client = factory.CreateClient("github");
    }
}
```

Warn when the containing service is known singleton or when assigned to static fields.

Suggested remediation:

```csharp
public sealed class MySingleton
{
    private readonly IHttpClientFactory _factory;

    public MySingleton(IHttpClientFactory factory)
    {
        _factory = factory;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/repos");
        var client = _factory.CreateClient("github");
        using var response = await client.SendAsync(request, cancellationToken);
    }
}
```

### `HCR004` - Do not inject typed clients into singleton services

Detect:

```csharp
services.AddHttpClient<PaymentsClient>();
services.AddSingleton<PaymentJob>();

public sealed class PaymentJob(PaymentsClient paymentsClient)
{
}
```

Suggested remediation options:

- Make the consuming service scoped/transient if appropriate.
- Inject `IHttpClientFactory` instead.
- Use a scoped service boundary.
- Use a long-lived manually configured client with `SocketsHttpHandler` where appropriate.

Do not provide an automatic code fix unless the lifetime change is obvious and safe.

### `HCR005` - Do not separately register typed clients

Detect:

```csharp
services.AddHttpClient<PaymentsClient>();
services.AddTransient<PaymentsClient>();
```

Fix:

```csharp
services.AddHttpClient<PaymentsClient>();
```

### `HCR020` - Delegating handlers should not capture scoped request data

Detect:

```csharp
public sealed class UserHeaderHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var userId = httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value;
        request.Headers.Add("X-User-Id", userId);
        return base.SendAsync(request, cancellationToken);
    }
}
```

Start with high-confidence dependencies:

- `IHttpContextAccessor`
- `HttpContext`
- `ClaimsPrincipal`
- `ISession`
- Known scoped services from the service registration graph

Suggested remediation:

- Pass request data through `HttpRequestMessage.Options`.
- Use a custom scope-aware wrapper outside the cached handler pipeline.
- Keep handlers stateless where possible.

### `HCR040` - Do not stack multiple standard resilience handlers

Detect:

```csharp
services.AddHttpClient<GitHubClient>()
    .AddStandardResilienceHandler()
    .AddStandardResilienceHandler();
```

Fix:

```csharp
services.AddHttpClient<GitHubClient>()
    .AddStandardResilienceHandler();
```

Also consider duplicate named pipeline registrations in later versions.

### `HCR041` - Unsafe HTTP methods should not be retried unless explicitly configured

Detect clients where:

- `AddStandardResilienceHandler()` is used.
- Retry configuration has not disabled unsafe HTTP methods.
- The typed client or named client sends unsafe methods.

Unsafe methods:

```text
POST
PUT
PATCH
DELETE
CONNECT
```

Potentially safe methods:

```text
GET
HEAD
OPTIONS
TRACE
```

Do not warn when:

- `DisableForUnsafeHttpMethods()` is used.
- Retry predicates explicitly exclude unsafe methods.
- The method is unknown and no outbound calls are visible.

### `HCR060` - Dispose response when using `ResponseHeadersRead`

Detect:

```csharp
var response = await client.SendAsync(
    request,
    HttpCompletionOption.ResponseHeadersRead,
    cancellationToken);

var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
```

Fix:

```csharp
using var response = await client.SendAsync(
    request,
    HttpCompletionOption.ResponseHeadersRead,
    cancellationToken);

var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
```

Avoid warning when:

- Response is returned to caller.
- Response is assigned to an owning wrapper.
- Response is disposed in a `finally` block.
- Response is used in a `using` statement/declaration.

### `HCR080` - High-concurrency HTTP fan-out should use bounded concurrency or connection limits

Detect obvious cases:

```csharp
await Task.WhenAll(items.Select(item => client.GetAsync(item.Url, cancellationToken)));
```

Suggested remediation:

```csharp
await Parallel.ForEachAsync(items, new ParallelOptions
{
    MaxDegreeOfParallelism = 8,
    CancellationToken = cancellationToken
}, async (item, ct) =>
{
    using var response = await client.GetAsync(item.Url, ct);
});
```

Keep as `Suggestion` initially because this rule is heuristic.

## `.editorconfig` profiles

### `profiles/default.editorconfig`

```ini
[*.cs]
dotnet_diagnostic.HCR001.severity = warning
dotnet_diagnostic.HCR002.severity = warning
dotnet_diagnostic.HCR003.severity = warning
dotnet_diagnostic.HCR004.severity = warning
dotnet_diagnostic.HCR005.severity = warning
dotnet_diagnostic.HCR020.severity = warning
dotnet_diagnostic.HCR040.severity = warning
dotnet_diagnostic.HCR041.severity = warning
dotnet_diagnostic.HCR060.severity = warning
dotnet_diagnostic.HCR080.severity = suggestion
```

### `profiles/brownfield-adoption.editorconfig`

```ini
[*.cs]
dotnet_diagnostic.HCR001.severity = suggestion
dotnet_diagnostic.HCR002.severity = suggestion
dotnet_diagnostic.HCR003.severity = suggestion
dotnet_diagnostic.HCR004.severity = suggestion
dotnet_diagnostic.HCR005.severity = suggestion
dotnet_diagnostic.HCR020.severity = suggestion
dotnet_diagnostic.HCR040.severity = warning
dotnet_diagnostic.HCR041.severity = warning
dotnet_diagnostic.HCR060.severity = warning
dotnet_diagnostic.HCR080.severity = silent
```

### `profiles/strict-ci.editorconfig`

```ini
[*.cs]
dotnet_diagnostic.HCR001.severity = error
dotnet_diagnostic.HCR002.severity = error
dotnet_diagnostic.HCR003.severity = error
dotnet_diagnostic.HCR004.severity = error
dotnet_diagnostic.HCR005.severity = error
dotnet_diagnostic.HCR020.severity = error
dotnet_diagnostic.HCR040.severity = error
dotnet_diagnostic.HCR041.severity = error
dotnet_diagnostic.HCR060.severity = error
dotnet_diagnostic.HCR080.severity = warning
```

## README starter copy

```markdown
# HttpClient.Resilience.Analyzers

Compile-time safety for `.NET` `HttpClient`, `IHttpClientFactory`, typed clients, retries, handlers, and outbound HTTP resilience.

## What it catches

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

## Example

```csharp
services.AddHttpClient<PaymentsClient>()
    .AddStandardResilienceHandler();
```

`HCR041` warns because the standard resilience pipeline may retry unsafe HTTP methods such as `POST`, `PUT`, `PATCH`, and `DELETE` unless configured otherwise.

```csharp
services.AddHttpClient<PaymentsClient>()
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.DisableForUnsafeHttpMethods();
    });
```
```

## Test strategy

Use analyzer tests for each rule covering:

- Happy path: no diagnostic.
- One obvious diagnostic.
- Multiple diagnostics in one file.
- Code fix output, where applicable.
- Top-level statements.
- Minimal API registrations.
- Traditional `Startup` registrations.
- Extension method registrations.
- Generic typed clients.
- Named clients.
- False-positive cases.

Example test naming convention:

```text
HCR041_WhenStandardResilienceHandlerRetriesPost_ReportsDiagnostic
HCR041_WhenUnsafeMethodsAreDisabled_DoesNotReport
HCR041_WhenOnlyGetIsUsed_DoesNotReport
HCR041_CodeFix_AddsDisableForUnsafeHttpMethods
```

## First implementation target

Implement this first:

```text
HCR060 - Dispose HttpResponseMessage when using ResponseHeadersRead
```

Why start here?

- It is local to a method.
- It does not require full DI graph analysis.
- It has a clean code fix.
- It proves the analyzer/code-fix/test infrastructure quickly.

Then implement:

```text
HCR001 - Do not create and dispose HttpClient per request
HCR041 - Unsafe HTTP methods should not be retried unless explicitly configured
```

This gives the project three strong demos:

1. Lifetime safety.
2. Response lifetime safety.
3. Resilience/retry safety.

## Launch checklist

- [ ] Package builds locally.
- [ ] Analyzer tests pass.
- [ ] Code-fix tests pass.
- [ ] README includes install instructions.
- [ ] README includes before/after examples.
- [ ] Each diagnostic has a docs page.
- [ ] Each diagnostic has suppression guidance.
- [ ] NuGet package contains analyzer DLL in the correct analyzer path.
- [ ] Package icon, license, repository URL, and tags are configured.
- [ ] CI runs build, test, pack.
- [ ] Sample project demonstrates every MVP rule.
- [ ] Publish `0.1.0-preview.1`.

## Future rule ideas

Do not build these until the MVP rules are stable:

| Rule ID | Idea |
|---|---|
| `HCR061` | Do not ignore unsuccessful HTTP responses. |
| `HCR062` | Prefer per-request headers over mutating shared `DefaultRequestHeaders`. |
| `HCR063` | Avoid sync-over-async around outbound HTTP. |
| `HCR064` | Use cancellation-aware HTTP APIs where available. |
| `HCR081` | Streaming responses should pass cancellation and dispose streams. |
| `HCR082` | Avoid per-request creation of resilience pipelines. |
| `HCR083` | Warn when typed clients have no `BaseAddress` and use relative URLs. |
| `HCR084` | Warn when named client names are stringly duplicated. |

## Reference docs to link from rule pages

- `HttpClient` guidelines for `.NET`
- `IHttpClientFactory` usage and troubleshooting
- `Microsoft.Extensions.Http.Resilience` documentation
- `Polly` retry and resilience documentation
- NuGet analyzer packaging conventions
- Roslyn analyzer testing documentation

## Recommended first issue

Title:

```text
Implement HCR060: Dispose HttpResponseMessage when using ResponseHeadersRead
```

Body:

```markdown
## Goal

Detect calls to `HttpClient.SendAsync` using `HttpCompletionOption.ResponseHeadersRead` where the returned `HttpResponseMessage` is not disposed.

## Bad

```csharp
var response = await client.SendAsync(
    request,
    HttpCompletionOption.ResponseHeadersRead,
    cancellationToken);

var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
```

## Good

```csharp
using var response = await client.SendAsync(
    request,
    HttpCompletionOption.ResponseHeadersRead,
    cancellationToken);

var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
```

## Acceptance criteria

- Reports diagnostic for a local response variable that is not disposed.
- Does not report when response is used in a `using var` declaration.
- Does not report when response is used in a `using` statement.
- Does not report when response is returned to the caller.
- Provides a code fix for simple local variable declarations.
- Adds docs page at `docs/rules/HCR060.md`.
```

## Final product north star

This package should become the thing a senior `.NET` engineer installs and says:

> “Yep. This catches the exact kind of `HttpClient` bugs that cost us incidents.”

Keep it practical. Keep it high-signal. Make every warning earn its place.
