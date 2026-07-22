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
    public async Task ReportsDiagnostic_WhenDirectHttpCallUsesRelativeUri()
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

            public sealed class PaymentsClient(HttpClient client)
            {
                public Task<HttpResponseMessage> SendAsync(CancellationToken cancellationToken)
                {
                    return client.GetAsync(
                        new Uri("/payments", UriKind.Relative),
                        cancellationToken);
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
    public async Task DoesNotReport_WhenDirectHttpCallUsesAbsoluteUri()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                }
            }

            public sealed class PaymentsClient(HttpClient client)
            {
                public HttpResponseMessage Send()
                {
                    return client.GetAsync(
                        new Uri("https://api.example.com/payments", UriKind.Absolute)).Result;
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
    public async Task ReportsDiagnostic_WhenDirectHttpCallUsesRelativeUriLocal()
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

            public sealed class PaymentsClient(HttpClient client)
            {
                public Task<HttpResponseMessage> SendAsync(CancellationToken cancellationToken)
                {
                    var requestUri = new Uri("/payments", UriKind.Relative);
                    return client.GetAsync(requestUri, cancellationToken);
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
    public async Task DoesNotReport_WhenRelativeUriLocalIsReassignedBeforeDirectHttpCall()
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

            public sealed class PaymentsClient(HttpClient client)
            {
                public Task<HttpResponseMessage> SendAsync(CancellationToken cancellationToken)
                {
                    var requestUri = new Uri("/payments", UriKind.Relative);
                    requestUri = new Uri("https://api.example.com/payments", UriKind.Absolute);
                    return client.GetAsync(requestUri, cancellationToken);
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
    public async Task ReportsDiagnostic_WhenDirectHttpCallUsesRelativeStringLocal()
    {
        const string source = """
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

            public sealed class PaymentsClient(HttpClient client)
            {
                public Task<HttpResponseMessage> SendAsync(CancellationToken cancellationToken)
                {
                    var path = "/payments";
                    return client.GetAsync(path, cancellationToken);
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
    public async Task DoesNotReport_WhenRelativeStringLocalIsReassignedBeforeDirectHttpCall()
    {
        const string source = """
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

            public sealed class PaymentsClient(HttpClient client)
            {
                public Task<HttpResponseMessage> SendAsync(CancellationToken cancellationToken)
                {
                    var path = "/payments";
                    path = "https://api.example.com/payments";
                    return client.GetAsync(path, cancellationToken);
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

    [Theory]
    [InlineData("DeleteFromJsonAsync")]
    [InlineData("GetFromJsonAsync")]
    public async Task ReportsDiagnostic_WhenJsonReadUsesRelativeUrl(string methodName)
    {
        var source = $$"""
            using System.Net.Http;
            using System.Net.Http.Json;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                }
            }

            public sealed class PaymentsClient(HttpClient client)
            {
                public Task<Order?> SendAsync(CancellationToken cancellationToken)
                {
                    return client.{{methodName}}<Order>("/payments", cancellationToken);
                }
            }

            public sealed class Order
            {
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

    [Theory]
    [InlineData("PatchAsJsonAsync")]
    [InlineData("PostAsJsonAsync")]
    [InlineData("PutAsJsonAsync")]
    public async Task ReportsDiagnostic_WhenJsonWriteUsesRelativeUrl(string methodName)
    {
        var source = $$"""
            using System.Net.Http;
            using System.Net.Http.Json;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                }
            }

            public sealed class PaymentsClient(HttpClient client)
            {
                public Task<HttpResponseMessage> SendAsync(Order order, CancellationToken cancellationToken)
                {
                    return client.{{methodName}}("/payments", order, cancellationToken);
                }
            }

            public sealed class Order
            {
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
    public async Task DoesNotReport_WhenCustomJsonExtensionUsesRelativeUrl()
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
                public static Task<T?> GetFromJsonAsync<T>(this HttpClient client, string value, int marker)
                {
                    return Task.FromResult(default(T));
                }
            }

            public sealed class PaymentsClient(HttpClient client)
            {
                public Task<Order?> SendAsync()
                {
                    return client.GetFromJsonAsync<Order>("/payments", 42);
                }
            }

            public sealed class Order
            {
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
    public async Task ReportsDiagnostic_WhenSendAsyncUsesInlineRequestWithRelativeUrl()
    {
        const string source = """
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

            public sealed class PaymentsClient(HttpClient client)
            {
                public Task<HttpResponseMessage> SendAsync(CancellationToken cancellationToken)
                {
                    return client.SendAsync(
                        new HttpRequestMessage(HttpMethod.Get, "/payments"),
                        cancellationToken);
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
    public async Task ReportsDiagnostic_WhenInlineRequestConstructorUsesRelativeUri()
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

            public sealed class PaymentsClient(HttpClient client)
            {
                public Task<HttpResponseMessage> SendAsync(CancellationToken cancellationToken)
                {
                    return client.SendAsync(
                        new HttpRequestMessage(
                            HttpMethod.Get,
                            new Uri("/payments", UriKind.Relative)),
                        cancellationToken);
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
    public async Task DoesNotReport_WhenInlineRequestConstructorUsesAbsoluteUri()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                }
            }

            public sealed class PaymentsClient(HttpClient client)
            {
                public HttpResponseMessage Send()
                {
                    return client.Send(
                        new HttpRequestMessage(
                            HttpMethod.Get,
                            new Uri("https://api.example.com/payments", UriKind.Absolute)));
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
    public async Task ReportsDiagnostic_WhenInlineRequestInitializerUsesRelativeRequestUri()
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

            public sealed class PaymentsClient(HttpClient client)
            {
                public Task<HttpResponseMessage> SendAsync(CancellationToken cancellationToken)
                {
                    return client.SendAsync(
                        new HttpRequestMessage
                        {
                            Method = HttpMethod.Get,
                            RequestUri = new Uri("/payments", UriKind.Relative),
                        },
                        cancellationToken);
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
    public async Task DoesNotReport_WhenInlineRequestInitializerUsesAbsoluteRequestUri()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                }
            }

            public sealed class PaymentsClient(HttpClient client)
            {
                public HttpResponseMessage Send()
                {
                    return client.Send(
                        new HttpRequestMessage
                        {
                            Method = HttpMethod.Get,
                            RequestUri = new Uri("https://api.example.com/payments", UriKind.Absolute),
                        });
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
    public async Task DoesNotReport_WhenSendUsesInlineRequestWithAbsoluteUrl()
    {
        const string source = """
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                }
            }

            public sealed class PaymentsClient(HttpClient client)
            {
                public HttpResponseMessage Send()
                {
                    return client.Send(
                        new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/payments"));
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
    public async Task ReportsDiagnostic_WhenSendAsyncUsesUnreassignedRequestLocalWithRelativeUrl()
    {
        const string source = """
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

            public sealed class PaymentsClient(HttpClient client)
            {
                public Task<HttpResponseMessage> SendAsync(CancellationToken cancellationToken)
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, "/payments");
                    return client.SendAsync(request, cancellationToken);
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
    public async Task DoesNotReport_WhenRelativeRequestLocalIsReassignedBeforeSendAsync()
    {
        const string source = """
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

            public sealed class PaymentsClient(HttpClient client)
            {
                public Task<HttpResponseMessage> SendAsync(CancellationToken cancellationToken)
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, "/payments");
                    request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/payments");
                    return client.SendAsync(request, cancellationToken);
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
    public async Task ReportsDiagnostic_WhenRequestLocalGetsRelativeRequestUriBeforeSendAsync()
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

            public sealed class PaymentsClient(HttpClient client)
            {
                public Task<HttpResponseMessage> SendAsync(CancellationToken cancellationToken)
                {
                    var request = new HttpRequestMessage();
                    request.RequestUri = new Uri("/payments", UriKind.Relative);
                    return client.SendAsync(request, cancellationToken);
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
    public async Task DoesNotReport_WhenRelativeConstructorUriIsReplacedByAbsoluteRequestUri()
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

            public sealed class PaymentsClient(HttpClient client)
            {
                public Task<HttpResponseMessage> SendAsync(CancellationToken cancellationToken)
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, "/payments");
                    request.RequestUri = new Uri("https://api.example.com/payments", UriKind.Absolute);
                    return client.SendAsync(request, cancellationToken);
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
    public async Task DoesNotReport_WhenRequestLocalIsReassignedAfterRelativeRequestUriAssignment()
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

            public sealed class PaymentsClient(HttpClient client)
            {
                public Task<HttpResponseMessage> SendAsync(CancellationToken cancellationToken)
                {
                    var request = new HttpRequestMessage();
                    request.RequestUri = new Uri("/payments", UriKind.Relative);
                    request = new HttpRequestMessage(
                        HttpMethod.Get,
                        "https://api.example.com/payments");
                    return client.SendAsync(request, cancellationToken);
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
    public async Task ReportsDiagnostic_WhenSendAsyncUsesSplitAssignedRequestLocalWithRelativeUrl()
    {
        const string source = """
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

            public sealed class PaymentsClient(HttpClient client)
            {
                public Task<HttpResponseMessage> SendAsync(CancellationToken cancellationToken)
                {
                    HttpRequestMessage request;
                    request = new HttpRequestMessage(HttpMethod.Get, "/payments");
                    return client.SendAsync(request, cancellationToken);
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
    public async Task DoesNotReport_WhenSplitAssignedRequestLocalIsReassignedBeforeSendAsync()
    {
        const string source = """
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

            public sealed class PaymentsClient(HttpClient client)
            {
                public Task<HttpResponseMessage> SendAsync(CancellationToken cancellationToken)
                {
                    HttpRequestMessage request;
                    request = new HttpRequestMessage(HttpMethod.Get, "/payments");
                    request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/payments");
                    return client.SendAsync(request, cancellationToken);
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
    public async Task ReportsDiagnostic_WhenServiceCollectionParameterUsesAlias()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;
            using Services = global::IServiceCollection;

            public static class Composition
            {
                public static void Configure(Services services)
                {
                    services.AddHttpClient<PaymentsClient>();
                }
            }

            public sealed class PaymentsClient
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
    public async Task ReportsDiagnostic_WhenInlineBaseAddressTargetsCustomClient()
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
                        client.BaseAddress = new Uri("https://api.example.com"));
                }
            }

            public sealed class PaymentsClient
            {
                public Task<string> SendAsync(HttpClient client)
                {
                    return client.GetStringAsync("/payments");
                }
            }

            public sealed class CustomClient
            {
                public Uri? BaseAddress { get; set; }
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
                    Action<CustomClient> configureClient)
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
    public async Task DoesNotReport_WhenBaseAddressIsConfiguredThroughBuilderLocal()
    {
        const string source = """
            using System;
            using System.Net.Http;
            using System.Threading.Tasks;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    var builder = services.AddHttpClient<PaymentsClient>();
                    builder.ConfigureHttpClient(client =>
                        client.BaseAddress = new Uri("https://api.example.com"));
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
    public async Task ReportsDiagnostic_WhenConfigureHttpClientIsCustomExtension()
    {
        const string source = """
            using System;
            using System.Net.Http;
            using System.Threading.Tasks;
            using CustomConfiguration;

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
            }

            namespace CustomConfiguration
            {
                public static class CustomHttpClientBuilderExtensions
                {
                    public static IHttpClientBuilder ConfigureHttpClient(
                        this IHttpClientBuilder builder,
                        Action<HttpClient> configureClient)
                    {
                        return builder;
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR083_TypedClientRelativeUrlWithoutBaseAddressAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR083, diagnostic.Id);
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

    [Fact]
    public async Task DoesNotReport_WhenAddHttpClientMethodIsOwnedByCustomNamespace()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;
            using Custom.DependencyInjection;

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
                    return client.GetStringAsync("/payments");
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            namespace Custom.DependencyInjection
            {
                public static class HttpClientBuilderExtensions
                {
                    public static global::IHttpClientBuilder AddHttpClient<TClient>(
                        this global::IServiceCollection services)
                    {
                        return default!;
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR083_TypedClientRelativeUrlWithoutBaseAddressAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }
}
