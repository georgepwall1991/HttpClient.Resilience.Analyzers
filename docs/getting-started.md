---
title: Getting started with the .NET HttpClient analyzer
description: Install HttpClient.Resilience.Analyzers with dotnet add package or Directory.Build.props, then build to see IHttpClientFactory and resilience diagnostics.
---

# Getting started

The analyzer runs at compile time. There is no service registration, middleware, or runtime configuration.

## Install one project

```bash
dotnet add package HttpClient.Resilience.Analyzers
```

Or add an explicit package reference:

```xml
<PackageReference Include="HttpClient.Resilience.Analyzers" Version="0.1.188" PrivateAssets="all" />
```

`PrivateAssets="all"` prevents the analyzer from flowing to projects that consume your project.

## Install a whole solution

Add the package once in `Directory.Build.props` at the repository root so every C# project gets the diagnostics:

```xml
<Project>
  <ItemGroup>
    <PackageReference Include="HttpClient.Resilience.Analyzers" Version="0.1.188">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

With Central Package Management, put the version in `Directory.Packages.props` and keep the `PackageReference` (without `Version`) in `Directory.Build.props`.

## Build and review

```bash
dotnet build
```

Diagnostics appear in Visual Studio, Rider, C# Dev Kit, and CI. Several rules include automatic code fixes. Every diagnostic links to a rule page with the risk, detection scope, safe alternatives, and suppression guidance.

## 30-second example

A typed client can send a non-idempotent `POST` through the standard retry pipeline:

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

`HCR041` reports the retry risk. `HCR043` reports the same class of incident for custom `AddResilienceHandler` pipelines that call `AddRetry`. If unsafe methods are not deliberately idempotent, disable their retries:

```csharp
services.AddHttpClient<PaymentsClient>()
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.DisableForUnsafeHttpMethods();
    });
```

## Choose a severity profile

Copy a profile from the package `contentFiles` or from the [repository profiles](https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers/tree/main/profiles):

| Profile | Best for |
|---|---|
| `default.editorconfig` | New services ready to act on production-safety warnings |
| `brownfield-adoption.editorconfig` | Existing applications that need a lower-noise first pass |
| `strict-ci.editorconfig` | Clean repositories that want findings to fail CI |
| `library-author.editorconfig` | Libraries with stricter response and stream ownership requirements |

See [configuration](configuration.md) and [adoption](adoption.md) for rollout details.
