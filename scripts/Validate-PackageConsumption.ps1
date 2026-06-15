param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PackagePath)) {
    throw "Package does not exist: $PackagePath"
}

$resolvedPackage = (Resolve-Path -LiteralPath $PackagePath).Path
$packageDirectory = Split-Path -Parent $resolvedPackage
$packageFileName = [System.IO.Path]::GetFileNameWithoutExtension($resolvedPackage)
$packageVersion = $packageFileName -replace '^HttpClient\.Resilience\.Analyzers\.', ''

if ([string]::IsNullOrWhiteSpace($packageVersion) -or $packageVersion -eq $packageFileName) {
    throw "Could not infer package version from $resolvedPackage."
}

$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('hcr-consume-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $tempDirectory | Out-Null

try {
    $projectPath = Join-Path $tempDirectory 'PackageConsumer.csproj'
    $programPath = Join-Path $tempDirectory 'Program.cs'
    $editorConfigPath = Join-Path $tempDirectory '.editorconfig'
    $packagesPath = Join-Path $tempDirectory '.packages'

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RestoreSources>$packageDirectory</RestoreSources>
    <RestorePackagesPath>$packagesPath</RestorePackagesPath>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="HttpClient.Resilience.Analyzers" Version="$packageVersion" PrivateAssets="all" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $projectPath

    @"
root = true

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
dotnet_diagnostic.HCR061.severity = warning
dotnet_diagnostic.HCR062.severity = warning
dotnet_diagnostic.HCR063.severity = warning
dotnet_diagnostic.HCR064.severity = warning
dotnet_diagnostic.HCR080.severity = warning
"@ | Set-Content -LiteralPath $editorConfigPath

    @"
using System.Net.Http;

public sealed class BadPaymentsService
{
    public async Task<string> SendAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        using var response = await client.GetAsync("https://example.com", cancellationToken);

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}

public sealed class BadStaticClient
{
    private static readonly HttpClient Client = new();

    public Task<HttpResponseMessage> GetAsync(CancellationToken cancellationToken)
    {
        return Client.GetAsync("https://example.com", cancellationToken);
    }
}

public sealed class BadFactoryClientCache
{
    private static HttpClient _client = null!;

    public static void Initialize(IHttpClientFactory factory)
    {
        _client = factory.CreateClient("github");
    }
}

public static class BadResilienceRegistration
{
    public static IHttpClientBuilder Configure(IServiceCollection services)
    {
        return services
            .AddHttpClient<BadPaymentsService>()
            .AddStandardResilienceHandler()
            .AddStandardResilienceHandler();
    }
}

public static class BadTypedClientLifetimeRegistration
{
    public static void Configure(IServiceCollection services)
    {
        services.AddHttpClient<BadPaymentsService>();
        services.AddSingleton<BadPaymentJob>();
    }
}

public static class BadDuplicateTypedClientRegistration
{
    public static void Configure(IServiceCollection services)
    {
        services.AddHttpClient<BadPaymentsService>();
        services.AddTransient<BadPaymentsService>();
    }
}

public static class BadUnsafeRetryRegistration
{
    public static IHttpClientBuilder Configure(IServiceCollection services)
    {
        return services
            .AddHttpClient<BadUnsafePaymentsClient>()
            .AddStandardResilienceHandler();
    }
}

public static class BadNamedUnsafeRetryRegistration
{
    public static IHttpClientBuilder Configure(IServiceCollection services)
    {
        return services
            .AddHttpClient("payments")
            .AddStandardResilienceHandler();
    }
}

public sealed class BadPaymentJob(BadPaymentsService paymentsService)
{
    public BadPaymentsService PaymentsService { get; } = paymentsService;
}

public sealed class BadUnsafePaymentsClient(HttpClient httpClient)
{
    public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
    {
        return httpClient.PostAsync("https://example.com/payments", null, cancellationToken);
    }
}

public sealed class BadNamedPaymentsJob(IHttpClientFactory factory)
{
    public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
    {
        var client = factory.CreateClient("payments");
        return client.PostAsync("https://example.com/payments", null, cancellationToken);
    }
}

public sealed class BadUserHeaderHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    public IHttpContextAccessor Accessor { get; } = accessor;
}

public sealed class BadFanOutService
{
    public Task SendAsync(HttpClient client, IEnumerable<string> urls, CancellationToken cancellationToken)
    {
        return Task.WhenAll(urls.Select(url => client.GetAsync(url, cancellationToken)));
    }
}

public sealed class BadStreamingResponseService
{
    public async Task<string> ReadAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}

public sealed class BadUncheckedResponseService
{
    public async Task<string> ReadAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync("https://example.com", cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}

public sealed class BadDefaultHeadersService
{
    public Task<HttpResponseMessage> SendAsync(HttpClient client, CancellationToken cancellationToken)
    {
        client.DefaultRequestHeaders.Add("X-Tenant", "northwind");
        return client.GetAsync("https://example.com", cancellationToken);
    }
}

public sealed class BadSyncOverAsyncService
{
    public HttpResponseMessage Send(HttpClient client)
    {
        return client.GetAsync("https://example.com").Result;
    }
}

public sealed class BadMissingCancellationService
{
    public Task<HttpResponseMessage> SendAsync(HttpClient client, CancellationToken cancellationToken)
    {
        return client.GetAsync("https://example.com");
    }
}

public interface IServiceCollection
{
}

public interface IHttpClientFactory
{
    HttpClient CreateClient(string name);
}

public interface IHttpContextAccessor
{
}

public interface IHttpClientBuilder
{
}

public static class HttpClientBuilderExtensions
{
    public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services)
    {
        return new DemoHttpClientBuilder();
    }

    public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name)
    {
        return new DemoHttpClientBuilder();
    }

    public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder)
    {
        return builder;
    }

    public static IServiceCollection AddSingleton<TService>(this IServiceCollection services)
    {
        return services;
    }

    public static IServiceCollection AddTransient<TService>(this IServiceCollection services)
    {
        return services;
    }

    private sealed class DemoHttpClientBuilder : IHttpClientBuilder
    {
    }
}
"@ | Set-Content -LiteralPath $programPath

    $restoreOutput = & dotnet restore $projectPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Output ($restoreOutput | Out-String)
        throw "Package consumer restore failed with exit code $LASTEXITCODE."
    }

    $buildOutput = & dotnet build $projectPath --configuration Release --no-restore 2>&1
    $buildExitCode = $LASTEXITCODE
    $buildText = $buildOutput | Out-String

    if ($buildExitCode -ne 0) {
        Write-Output $buildText
        throw "Package consumer build failed with exit code $buildExitCode."
    }

    $requiredDiagnostics = @(
        'HCR001',
        'HCR002',
        'HCR003',
        'HCR004',
        'HCR005',
        'HCR020',
        'HCR040',
        'HCR041',
        'HCR060',
        'HCR061',
        'HCR062',
        'HCR063',
        'HCR064',
        'HCR080'
    )

    $missingDiagnostics = @(
        foreach ($diagnostic in $requiredDiagnostics) {
            if ($buildText -notmatch "\b$diagnostic\b") {
                $diagnostic
            }
        }
    )

    if ($missingDiagnostics.Count -gt 0) {
        Write-Output $buildText
        throw "Package consumer build output is missing diagnostics: $($missingDiagnostics -join ', ')."
    }
}
finally {
    Remove-Item -LiteralPath $tempDirectory -Recurse -Force
}

"package consumption validation ok: HttpClient.Resilience.Analyzers $packageVersion emits all configured diagnostics"
