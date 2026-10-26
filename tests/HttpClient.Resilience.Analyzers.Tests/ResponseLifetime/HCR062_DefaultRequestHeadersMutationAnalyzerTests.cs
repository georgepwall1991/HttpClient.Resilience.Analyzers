using HttpClient.Resilience.Analyzers.Analyzers.ResponseLifetime;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.ResponseLifetime;

public sealed class HCR062_DefaultRequestHeadersMutationAnalyzerTests
{
    [Fact]
    public async Task DoesNotReport_InTestsNamespace()
    {
        const string source = """
            using System.Net.Http;

            namespace Payments.Tests
            {
                public sealed class Client
                {
                    public void Configure(HttpClient client)
                    {
                        client.DefaultRequestHeaders.Add("X-Test", "true");
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenDefaultRequestHeadersAddIsCalled()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Client
            {
                public void Configure(HttpClient client)
                {
                    client.DefaultRequestHeaders.Add("X-Tenant", "northwind");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR062, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenNestedDefaultRequestHeadersCollectionIsMutated()
    {
        const string source = """
            using System.Net.Http;
            using System.Net.Http.Headers;

            public sealed class Client
            {
                public void Configure(HttpClient client)
                {
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR062, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenDefaultRequestHeadersPropertyIsAssigned()
    {
        const string source = """
            using System.Net.Http;
            using System.Net.Http.Headers;

            public sealed class Client
            {
                public void Configure(HttpClient client)
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR062, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenRequestMessageHeadersAreMutated()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Client
            {
                public void Configure(HttpRequestMessage request)
                {
                    request.Headers.Add("X-Tenant", "northwind");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenDefaultRequestHeadersAreOnlyRead()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Client
            {
                public bool HasHeader(HttpClient client)
                {
                    return client.DefaultRequestHeaders.Contains("X-Tenant");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResolvedCustomHttpClientHasDefaultRequestHeaders()
    {
        const string source = """
            public sealed class Client
            {
                public void Configure(Custom.HttpClient client)
                {
                    client.DefaultRequestHeaders.Add("X-Tenant", "northwind");
                }
            }

            namespace Custom
            {
                public sealed class HttpClient
                {
                    public Headers DefaultRequestHeaders { get; } = new();
                }

                public sealed class Headers
                {
                    public void Add(string name, string value)
                    {
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenCustomExtensionNamedAddTargetsRealDefaultHeaders()
    {
        const string source = """
            using System.Net.Http;
            using System.Net.Http.Headers;

            public static class CustomHeaderExtensions
            {
                public static void Add(this HttpRequestHeaders headers, int marker)
                {
                }
            }

            public sealed class Client
            {
                public void Configure(HttpClient client)
                {
                    client.DefaultRequestHeaders.Add(42);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenThisQualifiedHttpClientFieldHeadersAreMutated()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Client
            {
                private readonly HttpClient _client = new();

                public void Configure()
                {
                    this._client.DefaultRequestHeaders.TryAddWithoutValidation("X-Tenant", "northwind");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR062, diagnostic.Id);
    }
    [Fact]
    public async Task DoesNotReport_WhenHeadersAreSetInsideAddHttpClientConfigureDelegate()
    {
        var source = $$"""
            using System.Net.Http;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services.AddHttpClient("payments", client =>
                        client.DefaultRequestHeaders.Add("X-Tenant", "northwind"));
                }
            }

            {{CustomPipelineSources.FrameworkStubs}}
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenHeadersAreSetInsideConfigureHttpClientDelegate()
    {
        var source = $$"""
            using System.Net.Http;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient("payments")
                        .ConfigureHttpClient(client =>
                            client.DefaultRequestHeaders.Add("X-Tenant", "northwind"));
                }
            }

            {{CustomPipelineSources.FrameworkStubs}}
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenHeadersAreSetInsideRegisteredTypedClientConstructor()
    {
        var source = $$"""
            using System.Net.Http;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services.AddHttpClient<PaymentsClient>();
                }
            }

            public sealed class PaymentsClient
            {
                public PaymentsClient(HttpClient client)
                {
                    client.DefaultRequestHeaders.Add("X-Tenant", "northwind");
                }
            }

            {{CustomPipelineSources.FrameworkStubs}}
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenConfigureDelegateMutatesADifferentClient()
    {
        var source = $$"""
            using System.Net.Http;

            public static class Registrations
            {
                private static readonly HttpClient Shared = new HttpClient();

                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services.AddHttpClient("payments", client =>
                        Shared.DefaultRequestHeaders.Add("X-Tenant", "northwind"));
                }
            }

            {{CustomPipelineSources.FrameworkStubs}}
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR062, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenUnregisteredConstructorMutatesHeaders()
    {
        var source = $$"""
            using System.Net.Http;

            public sealed class PaymentsClient
            {
                public PaymentsClient(HttpClient client)
                {
                    client.DefaultRequestHeaders.Add("X-Tenant", "northwind");
                }
            }

            {{CustomPipelineSources.FrameworkStubs}}
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR062, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenConstructorConfiguresClientItCreates()
    {
        const string source = """
            using System.Net.Http;

            public sealed class NuGetVersionLookupService
            {
                private readonly HttpClient _httpClient;

                public NuGetVersionLookupService(HttpClient? httpClient = null)
                {
                    _httpClient = httpClient ?? new HttpClient();
                    _httpClient.DefaultRequestHeaders.Add("User-Agent", "CPMigrate-CLI");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenConstructorConfiguresClientCreatedInFieldInitializer()
    {
        const string source = """
            using System.Net.Http;

            public sealed class NuGetVersionLookupService
            {
                private readonly HttpClient _httpClient = new HttpClient();

                public NuGetVersionLookupService()
                {
                    _httpClient.DefaultRequestHeaders.Add("User-Agent", "CPMigrate-CLI");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenFreshClientIsConfiguredBeforeReturn()
    {
        const string source = """
            using System.Net.Http;

            public static class ClientFactory
            {
                public static HttpClient Create()
                {
                    var client = new HttpClient();
                    client.DefaultRequestHeaders.Add("X-Api-Version", "2026-01-01");
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenFactoryCreatedClientIsConfiguredBeforeReturn()
    {
        const string source = """
            using System.Net.Http;

            public static class ClientFactory
            {
                public static HttpClient Create(IHttpClientFactory factory)
                {
                    var client = factory.CreateClient();
                    client.DefaultRequestHeaders.Add("X-Api-Version", "2026-01-01");
                    return client;
                }
            }

            namespace System.Net.Http
            {
                public interface IHttpClientFactory
                {
                    HttpClient CreateClient(string name);
                }

                public static class HttpClientFactoryExtensions
                {
                    public static HttpClient CreateClient(this IHttpClientFactory factory) =>
                        factory.CreateClient(string.Empty);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenFactoryCreatedClientIsConfiguredInBranchBeforeReturn()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Net.Http;

            public static class DaprDefaults
            {
                public static HttpClient CreateDefaultHttpClient(IHttpClientFactory httpClientFactory, bool hasToken)
                {
                    var httpClient = httpClientFactory.CreateClient();
                    httpClient.BaseAddress = new Uri("https://localhost:3500");

                    KeyValuePair<string, string>? apiTokenHeader = hasToken
                        ? new KeyValuePair<string, string>("dapr-api-token", "secret")
                        : null;
                    if (apiTokenHeader is not null)
                    {
                        httpClient.DefaultRequestHeaders.Add(apiTokenHeader.Value.Key, apiTokenHeader.Value.Value);
                    }

                    return httpClient;
                }
            }

            namespace System.Net.Http
            {
                public interface IHttpClientFactory
                {
                    HttpClient CreateClient(string name);
                }

                public static class HttpClientFactoryExtensions
                {
                    public static HttpClient CreateClient(this IHttpClientFactory factory) =>
                        factory.CreateClient(string.Empty);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenNamedFactoryClientIsConfiguredBeforeReturn()
    {
        const string source = """
            using System.Net.Http;

            public static class ClientFactory
            {
                public static HttpClient Create(IHttpClientFactory factory)
                {
                    var client = factory.CreateClient("payments");
                    client.DefaultRequestHeaders.Add("X-Api-Version", "2026-01-01");
                    return client;
                }
            }

            namespace System.Net.Http
            {
                public interface IHttpClientFactory
                {
                    HttpClient CreateClient(string name);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenFreshLocalClientIsConfiguredAndUsed()
    {
        const string source = """
            using System.Net.Http;
            using System.Net.Http.Headers;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class PaymentsClient
            {
                private readonly IHttpClientFactory _factory;

                public PaymentsClient(IHttpClientFactory factory)
                {
                    _factory = factory;
                }

                public async Task SendAsync(CancellationToken cancellationToken)
                {
                    var client = _factory.CreateClient();
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token");
                    await client.GetAsync("https://example.com", cancellationToken);
                }
            }

            namespace System.Net.Http
            {
                public interface IHttpClientFactory
                {
                    HttpClient CreateClient(string name);
                }

                public static class HttpClientFactoryExtensions
                {
                    public static HttpClient CreateClient(this IHttpClientFactory factory) =>
                        factory.CreateClient(string.Empty);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenFreshLocalClientIsConstructedAndUsed()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;

            public sealed class PaymentsClient
            {
                public Task SendAsync()
                {
                    var client = new HttpClient();
                    client.DefaultRequestHeaders.Add("X-Tenant", "northwind");
                    return client.GetAsync("https://example.com");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenFreshLocalClientIsPassedToAnotherMethod()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;

            public sealed class PaymentsClient
            {
                public async Task SendAsync()
                {
                    var client = new HttpClient();
                    Configure(client);
                    client.DefaultRequestHeaders.Add("X-Tenant", "northwind");
                    await client.GetAsync("https://example.com");
                }

                private static void Configure(HttpClient client)
                {
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR062, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenFreshLocalClientIsStoredInField()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;

            public sealed class PaymentsClient
            {
                private HttpClient? _client;

                public async Task SendAsync()
                {
                    var client = new HttpClient();
                    _client = client;
                    client.DefaultRequestHeaders.Add("X-Tenant", "northwind");
                    await client.GetAsync("https://example.com");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR062, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenFreshLocalClientIsReassignedBeforeConfiguration()
    {
        const string source = """
            using System.Net.Http;

            public sealed class PaymentsClient
            {
                private readonly HttpClient _shared = new HttpClient();

                public void Configure()
                {
                    var client = new HttpClient();
                    client = _shared;
                    client.DefaultRequestHeaders.Add("X-Tenant", "northwind");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR062, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenInjectedClientIsConfiguredAndReturned()
    {
        const string source = """
            using System.Net.Http;

            public static class ClientFactory
            {
                public static HttpClient Configure(HttpClient client)
                {
                    client.DefaultRequestHeaders.Add("X-Api-Version", "2026-01-01");
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR062, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenMethodConfiguresClientConstructedInConstructor()
    {
        const string source = """
            using System.Net.Http;

            public sealed class NuGetVersionLookupService
            {
                private readonly HttpClient _httpClient = new HttpClient();

                public void Configure()
                {
                    _httpClient.DefaultRequestHeaders.Add("User-Agent", "CPMigrate-CLI");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR062, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenConstructorConfiguresStaticClient()
    {
        const string source = """
            using System.Net.Http;

            public sealed class NuGetVersionLookupService
            {
                private static readonly HttpClient Shared = new HttpClient();

                public NuGetVersionLookupService()
                {
                    Shared.DefaultRequestHeaders.Add("User-Agent", "CPMigrate-CLI");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR062, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenConstructorConfiguresInjectedClientRetainedInField()
    {
        const string source = """
            using System.Net.Http;

            public sealed class NuGetVersionLookupService
            {
                private readonly HttpClient _httpClient;

                public NuGetVersionLookupService(HttpClient httpClient)
                {
                    _httpClient = httpClient;
                    _httpClient.DefaultRequestHeaders.Add("User-Agent", "CPMigrate-CLI");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR062, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenStaticConstructorConfiguresClientItCreates()
    {
        const string source = """
            using System.Net.Http;

            public sealed class ApiClient
            {
                private static readonly HttpClient Shared;

                static ApiClient()
                {
                    Shared = new HttpClient();
                    Shared.DefaultRequestHeaders.Add("User-Agent", "HCR-Tests");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR062_DefaultRequestHeadersMutationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }
}
