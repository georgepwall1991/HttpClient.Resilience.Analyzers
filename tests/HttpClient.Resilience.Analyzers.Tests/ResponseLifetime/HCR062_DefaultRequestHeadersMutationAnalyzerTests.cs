using HttpClient.Resilience.Analyzers.Analyzers.ResponseLifetime;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.ResponseLifetime;

public sealed class HCR062_DefaultRequestHeadersMutationAnalyzerTests
{
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

}
