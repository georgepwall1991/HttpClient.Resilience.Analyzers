using HttpClient.Resilience.Analyzers.Analyzers.Resilience;
using HttpClient.Resilience.Analyzers.CodeFixes;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.Resilience;

public sealed class HCR041_UnsafeMethodRetryAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenStandardResilienceHandlerIsUsedWithUnsafeTypedClientCall()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<PaymentsClient>()
                        .AddStandardResilienceHandler();
                }
            }

            public sealed class PaymentsClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return httpClient.PostAsync("/payments", null, cancellationToken);
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services) => null!;
                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR041, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnsafeMethodsAreDisabled()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<PaymentsClient>()
                        .AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());
                }
            }

            public sealed class PaymentsClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return httpClient.PostAsync("/payments", null, cancellationToken);
                }
            }

            public sealed class StandardHttpResilienceOptions
            {
                public RetryOptions Retry { get; } = new();
            }

            public sealed class RetryOptions
            {
                public void DisableForUnsafeHttpMethods()
                {
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services) => null!;
                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder, System.Action<StandardHttpResilienceOptions> configure) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenRetryPredicateOnlyAllowsSafeMethods()
    {
        const string source = """
            using System;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<PaymentsClient>()
                        .AddStandardResilienceHandler(options =>
                        {
                            options.Retry.ShouldHandle = args =>
                                args.Outcome.Result?.RequestMessage?.Method == HttpMethod.Get;
                        });
                }
            }

            public sealed class PaymentsClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return httpClient.PostAsync("/payments", null, cancellationToken);
                }
            }

            public sealed class StandardHttpResilienceOptions
            {
                public RetryOptions Retry { get; } = new();
            }

            public sealed class RetryOptions
            {
                public Func<RetryPredicateArguments, bool>? ShouldHandle { get; set; }
            }

            public sealed class RetryPredicateArguments
            {
                public Outcome Outcome { get; } = new();
            }

            public sealed class Outcome
            {
                public HttpResponseMessage? Result { get; set; }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services) => null!;
                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder, Action<StandardHttpResilienceOptions> configure) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenRetryPredicateStillAllowsUnsafeMethod()
    {
        const string source = """
            using System;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<PaymentsClient>()
                        .AddStandardResilienceHandler(options =>
                        {
                            options.Retry.ShouldHandle = args =>
                                args.Outcome.Result?.RequestMessage?.Method == HttpMethod.Post;
                        });
                }
            }

            public sealed class PaymentsClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return httpClient.PostAsync("/payments", null, cancellationToken);
                }
            }

            public sealed class StandardHttpResilienceOptions
            {
                public RetryOptions Retry { get; } = new();
            }

            public sealed class RetryOptions
            {
                public Func<RetryPredicateArguments, bool>? ShouldHandle { get; set; }
            }

            public sealed class RetryPredicateArguments
            {
                public Outcome Outcome { get; } = new();
            }

            public sealed class Outcome
            {
                public HttpResponseMessage? Result { get; set; }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services) => null!;
                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder, Action<StandardHttpResilienceOptions> configure) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR041, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenTypedClientOnlySendsSafeHttpMethods()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<PaymentsClient>()
                        .AddStandardResilienceHandler();
                }
            }

            public sealed class PaymentsClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> GetAsync(CancellationToken cancellationToken)
                {
                    return httpClient.GetAsync("/payments", cancellationToken);
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services) => null!;
                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTypedClientSendsUnsafeHttpRequestMessage()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<PaymentsClient>()
                        .AddStandardResilienceHandler();
                }
            }

            public sealed class PaymentsClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, "/payments");
                    return httpClient.SendAsync(request, cancellationToken);
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services) => null!;
                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR041, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTypedClientSendsUnsafeHttpRequestMessageInitializer()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<PaymentsClient>()
                        .AddStandardResilienceHandler();
                }
            }

            public sealed class PaymentsClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return httpClient.SendAsync(new HttpRequestMessage { Method = HttpMethod.Delete }, cancellationToken);
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services) => null!;
                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR041, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenTypedClientSendsSafeHttpRequestMessage()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<PaymentsClient>()
                        .AddStandardResilienceHandler();
                }
            }

            public sealed class PaymentsClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> GetAsync(CancellationToken cancellationToken)
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, "/payments");
                    return httpClient.SendAsync(request, cancellationToken);
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services) => null!;
                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTypedClientRegistrationAndUnsafeCallAreInDifferentFiles()
    {
        const string registrations = """
            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<PaymentsClient>()
                        .AddStandardResilienceHandler();
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services) => null!;
                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        const string client = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class PaymentsClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return httpClient.PostAsync("/payments", null, cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(registrations, client);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR041, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenNamedClientWithStandardResilienceSendsUnsafeMethod()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient("payments")
                        .AddStandardResilienceHandler();
                }
            }

            public sealed class PaymentJob(IHttpClientFactory factory)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    var client = factory.CreateClient("payments");
                    return client.PostAsync("/payments", null, cancellationToken);
                }
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name) => null!;
                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR041, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenNamedClientSendsUnsafeHttpRequestMessage()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient("payments")
                        .AddStandardResilienceHandler();
                }
            }

            public sealed class PaymentJob(IHttpClientFactory factory)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    var client = factory.CreateClient("payments");
                    return client.SendAsync(new HttpRequestMessage(HttpMethod.Put, "/payments/42"), cancellationToken);
                }
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name) => null!;
                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR041, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenNamedClientRegistrationAndUnsafeCallAreInDifferentFiles()
    {
        const string registrations = """
            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient("payments")
                        .AddStandardResilienceHandler();
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name) => null!;
                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        const string job = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class PaymentJob(IHttpClientFactory factory)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    var client = factory.CreateClient("payments");
                    return client.PostAsync("/payments", null, cancellationToken);
                }
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(registrations, job);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR041, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenNamedClientCreateClientCallIsChainedToUnsafeMethod()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient("payments")
                        .AddStandardResilienceHandler();
                }
            }

            public sealed class PaymentJob(IHttpClientFactory factory)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return factory.CreateClient("payments").PostAsync("/payments", null, cancellationToken);
                }
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name) => null!;
                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR041, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnsafeCallUsesDifferentNamedClient()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient("catalog")
                        .AddStandardResilienceHandler();
                }
            }

            public sealed class PaymentJob(IHttpClientFactory factory)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    var client = factory.CreateClient("payments");
                    return client.PostAsync("/payments", null, cancellationToken);
                }
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name) => null!;
                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task CodeFix_AddsDisableForUnsafeHttpMethodsConfiguration()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<PaymentsClient>()
                        .AddStandardResilienceHandler();
                }
            }

            public sealed class PaymentsClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return httpClient.PostAsync("/payments", null, cancellationToken);
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services) => null!;
                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR041_UnsafeMethodRetryAnalyzer, HCR041_DisableUnsafeMethodRetriesCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(".AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods())", fixedSource);
    }
}
