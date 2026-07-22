using HttpClient.Resilience.Analyzers.Analyzers.TypedClients;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.TypedClients;

public sealed class HCR083_TypedClientRelativeUrlWithoutBaseAddressAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenRegisteredTypedClientUsesRelativeUrlWithoutBaseAddress()
    {
        const string source = """
            using System;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                }
            }

            public sealed class PaymentsClient
            {
                private readonly HttpClient _client;

                public PaymentsClient(HttpClient client)
                {
                    _client = client;
                }

                public Task<HttpResponseMessage> SendAsync(CancellationToken cancellationToken)
                {
                    return _client.GetAsync("/payments", cancellationToken);
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR083_TypedClientRelativeUrlWithoutBaseAddressAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR083, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTwoGenericTypedClientImplementationUsesRelativeUrl()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<IPaymentsClient, PaymentsClient>();
                }
            }

            public interface IPaymentsClient
            {
            }

            public sealed class PaymentsClient : IPaymentsClient
            {
                public Task<string> SendAsync(HttpClient client)
                {
                    return client.GetStringAsync("payments");
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TClient, TImplementation>(this IServiceCollection services)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR083_TypedClientRelativeUrlWithoutBaseAddressAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR083, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenBaseAddressIsConfiguredInAddHttpClient()
    {
        const string source = """
            using System;
            using System.Net.Http;
            using System.Threading.Tasks;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>(client =>
                    {
                        client.BaseAddress = new Uri("https://api.example.com");
                    });
                }
            }

            public sealed class PaymentsClient
            {
                public Task<string> SendAsync(HttpClient client)
                {
                    return client.GetStringAsync("/payments");
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TClient>(
                    this IServiceCollection services,
                    Action<HttpClient> configureClient)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR083_TypedClientRelativeUrlWithoutBaseAddressAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenBaseAddressIsConfiguredInConfigureHttpClientChain()
    {
        const string source = """
            using System;
            using System.Net.Http;
            using System.Threading.Tasks;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services
                        .AddHttpClient<PaymentsClient>()
                        .ConfigureHttpClient(client => client.BaseAddress = new Uri("https://api.example.com"));
                }
            }

            public sealed class PaymentsClient
            {
                public Task<string> SendAsync(HttpClient client)
                {
                    return client.GetStringAsync("/payments");
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services)
                {
                    return default!;
                }

                public static IHttpClientBuilder ConfigureHttpClient(
                    this IHttpClientBuilder builder,
                    Action<HttpClient> configureClient)
                {
                    return builder;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR083_TypedClientRelativeUrlWithoutBaseAddressAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenTypedClientUsesAbsoluteUrl()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                }
            }

            public sealed class PaymentsClient
            {
                public Task<string> SendAsync(HttpClient client)
                {
                    return client.GetStringAsync("https://api.example.com/payments");
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR083_TypedClientRelativeUrlWithoutBaseAddressAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenTypeIsNotRegisteredTypedClient()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;

            public sealed class PaymentsClient
            {
                public Task<string> SendAsync(HttpClient client)
                {
                    return client.GetStringAsync("/payments");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR083_TypedClientRelativeUrlWithoutBaseAddressAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResolvedCustomHttpClientUsesRelativeUrl()
    {
        const string source = """
            using System.Threading.Tasks;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                }
            }

            public sealed class PaymentsClient
            {
                public Task<string> SendAsync(Custom.HttpClient client)
                {
                    return client.GetStringAsync("/payments");
                }
            }

            namespace Custom
            {
                public sealed class HttpClient
                {
                    public Task<string> GetStringAsync(string url)
                    {
                        return Task.FromResult(url);
                    }
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR083_TypedClientRelativeUrlWithoutBaseAddressAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenCustomExtensionOnHttpClientUsesRelativeString()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                }
            }

            public static class CustomHttpClientExtensions
            {
                public static Task<string> GetStringAsync(this HttpClient client, string value, int marker)
                {
                    return Task.FromResult(value);
                }
            }

            public sealed class PaymentsClient
            {
                public Task<string> SendAsync(HttpClient client)
                {
                    return client.GetStringAsync("/payments", 42);
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR083_TypedClientRelativeUrlWithoutBaseAddressAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }
}
