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
    public async Task DoesNotReport_WhenStandardResilienceHandlerIsCustomExtension()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;
            using CustomResilience;

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
            }

            namespace CustomResilience
            {
                public static class CustomHttpClientBuilderExtensions
                {
                    public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder) => builder;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTypedClientSendsConnectMethodThroughHttpClientExtension()
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
                        .AddHttpClient<TunnelClient>()
                        .AddStandardResilienceHandler();
                }
            }

            public sealed class TunnelClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> ConnectAsync(CancellationToken cancellationToken)
                {
                    return httpClient.ConnectAsync("/tunnel", cancellationToken);
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

            public static class HttpClientConnectExtensions
            {
                public static Task<HttpResponseMessage> ConnectAsync(
                    this HttpClient client,
                    string route,
                    CancellationToken cancellationToken)
                {
                    return client.SendAsync(new HttpRequestMessage(HttpMethod.Connect, route), cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR041, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_InMinimalHostingStyleConfiguration()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            var builder = WebApplication.CreateBuilder(args);

            builder.Services
                .AddHttpClient<PaymentsClient>()
                .AddStandardResilienceHandler();

            public sealed class PaymentsClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return httpClient.PostAsync("/payments", null, cancellationToken);
                }
            }

            public sealed class WebApplication
            {
                public IServiceCollection Services { get; } = null!;
                public static WebApplication CreateBuilder(string[] args) => null!;
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
    public async Task ReportsDiagnostic_WhenTypedClientStandardResilienceHandlerIsSplitAcrossBuilderLocal()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    var builder = services.AddHttpClient<PaymentsClient>();
                    builder.AddStandardResilienceHandler();
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
    public async Task DoesNotReport_WhenTypedClientBuilderLocalIsReassignedBeforeStandardHandler()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    var builder = services.AddHttpClient<PaymentsClient>();
                    builder = services.AddHttpClient<CatalogClient>();
                    builder.AddStandardResilienceHandler();
                }
            }

            public sealed class PaymentsClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return httpClient.PostAsync("/payments", null, cancellationToken);
                }
            }

            public sealed class CatalogClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> GetAsync(CancellationToken cancellationToken)
                {
                    return httpClient.GetAsync("/catalog", cancellationToken);
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
    public async Task ReportsDiagnostic_WhenTypedClientUsesThisQualifiedHttpClientField()
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

            public sealed class PaymentsClient
            {
                private readonly HttpClient _client;

                public PaymentsClient(HttpClient client)
                {
                    _client = client;
                }

                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return this._client.PostAsync("/payments", null, cancellationToken);
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
    public async Task ReportsDiagnostic_WhenQualifiedTypedClientSendsUnsafeMethod()
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
                        .AddHttpClient<Clients.PaymentsClient>()
                        .AddStandardResilienceHandler();
                }
            }

            namespace Clients
            {
                public sealed class PaymentsClient(HttpClient httpClient)
                {
                    public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                    {
                        return httpClient.PostAsync("/payments", null, cancellationToken);
                    }
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
    public async Task ReportsDiagnostic_WhenUnqualifiedTypedClientResolvesInNamespaceAndSendsUnsafeMethod()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;

            namespace Clients
            {
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

            }

            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ServiceCollectionExtensions
                {
                    public static Clients.IHttpClientBuilder AddHttpClient<TClient>(this Clients.IServiceCollection services) => null!;
                    public static Clients.IHttpClientBuilder AddStandardResilienceHandler(this Clients.IHttpClientBuilder builder) => builder;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR041, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnqualifiedTypedClientResolvesToDifferentSameNamedType()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Clients
            {
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
            }

            namespace Other
            {
                public sealed class PaymentsClient(HttpClient httpClient)
                {
                    public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                    {
                        return httpClient.PostAsync("/payments", null, cancellationToken);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTwoGenericTypedClientImplementationSendsUnsafeMethod()
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
                        .AddHttpClient<IPaymentsClient, PaymentsClient>()
                        .AddStandardResilienceHandler();
                }
            }

            public interface IPaymentsClient
            {
            }

            public sealed class PaymentsClient(HttpClient httpClient) : IPaymentsClient
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
                public static IHttpClientBuilder AddHttpClient<TService, TImplementation>(this IServiceCollection services) => null!;
                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR041, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenTwoGenericQualifiedImplementationTargetsDifferentSameNamedType()
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
                        .AddHttpClient<IPaymentsClient, Clients.PaymentsClient>()
                        .AddStandardResilienceHandler();
                }
            }

            public interface IPaymentsClient
            {
            }

            namespace Other
            {
                public sealed class PaymentsClient(HttpClient httpClient) : IPaymentsClient
                {
                    public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                    {
                        return httpClient.PostAsync("/payments", null, cancellationToken);
                    }
                }
            }

            namespace Clients
            {
                public sealed class PaymentsClient(HttpClient httpClient) : IPaymentsClient
                {
                    public Task<HttpResponseMessage> GetAsync(CancellationToken cancellationToken)
                    {
                        return httpClient.GetAsync("/payments", cancellationToken);
                    }
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
                public static IHttpClientBuilder AddHttpClient<TService, TImplementation>(this IServiceCollection services) => null!;
                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenQualifiedTypedClientTargetsDifferentSameNamedType()
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
                        .AddHttpClient<Clients.PaymentsClient>()
                        .AddStandardResilienceHandler();
                }
            }

            namespace Other
            {
                public sealed class PaymentsClient(HttpClient httpClient)
                {
                    public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                    {
                        return httpClient.PostAsync("/payments", null, cancellationToken);
                    }
                }
            }

            namespace Clients
            {
                public sealed class PaymentsClient(HttpClient httpClient)
                {
                    public Task<HttpResponseMessage> GetAsync(CancellationToken cancellationToken)
                    {
                        return httpClient.GetAsync("/payments", cancellationToken);
                    }
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
    public async Task DoesNotReport_WhenTypedClientCallsLookalikeUnsafeMethod()
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

            public sealed class PaymentsClient(PaymentGateway gateway)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return gateway.PostAsync("/payments", cancellationToken);
                }
            }

            public sealed class PaymentGateway
            {
                public Task<HttpResponseMessage> PostAsync(string route, CancellationToken cancellationToken)
                {
                    return Task.FromResult(new HttpResponseMessage());
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
    public async Task DoesNotReport_WhenTypedClientUsesQualifiedCustomHttpClient()
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

            public sealed class PaymentsClient(Custom.HttpClient httpClient)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return httpClient.PostAsync("/payments", null, cancellationToken);
                }
            }

            namespace Custom
            {
                public sealed class HttpClient
                {
                    public Task<HttpResponseMessage> PostAsync(string route, HttpContent? content, CancellationToken cancellationToken)
                    {
                        return Task.FromResult(new HttpResponseMessage());
                    }
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
    public async Task DoesNotReport_WhenLookalikeTypedClientRegistrationIsNotIServiceCollection()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static CustomBuilder Configure(CustomServices services)
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

            public sealed class CustomServices
            {
            }

            public sealed class CustomBuilder
            {
            }

            public static class CustomBuilderExtensions
            {
                public static CustomBuilder AddHttpClient<TClient>(this CustomServices services) => new();
                public static CustomBuilder AddStandardResilienceHandler(this CustomBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
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
    public async Task ReportsDiagnostic_WhenUnsafeMethodGuardIsCustomExtension()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;
            using CustomRetryGuards;

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

            namespace CustomRetryGuards
            {
                public static class RetryOptionsExtensions
                {
                    public static void DisableForUnsafeHttpMethods(this RetryOptions options)
                    {
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR041, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnresolvedBuilderServicesSharesServiceCollectionPropertyName()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            var builder = UnknownApplication.CreateBuilder(args);

            builder.Services
                .AddHttpClient<PaymentsClient>()
                .AddStandardResilienceHandler();

            public sealed class PaymentsClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return httpClient.PostAsync("/payments", null, cancellationToken);
                }
            }

            public sealed class RealApplication
            {
                public IServiceCollection Services { get; } = null!;
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

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

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
    public async Task DoesNotReport_WhenRetryPredicateUsesQualifiedSafeHttpMethod()
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
                                args.Outcome.Result?.RequestMessage?.Method == System.Net.Http.HttpMethod.Get;
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
    public async Task DoesNotReport_WhenRetryPredicateUsesNullForgivingSafeMethod()
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
                                (args.Outcome.Result?.RequestMessage?.Method == HttpMethod.Get!)!;
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
    public async Task DoesNotReport_WhenRetryPredicateUsesSafeHttpMethodEquals()
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
                                HttpMethod.Get.Equals(args.Outcome.Result?.RequestMessage?.Method);
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
    public async Task DoesNotReport_WhenRetryPredicateUsesObjectEqualsWithSafeMethod()
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
                                object.Equals(args.Outcome.Result?.RequestMessage?.Method, HttpMethod.Get);
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
    public async Task DoesNotReport_WhenBlockRetryPredicateOnlyAllowsSafeMethod()
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
                            {
                                return args.Outcome.Result?.RequestMessage?.Method == HttpMethod.Get;
                            };
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
    public async Task ReportsDiagnostic_WhenRetryPredicateExcludesOnlySafeMethod()
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
                                args.Outcome.Result?.RequestMessage?.Method != HttpMethod.Get;
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
    public async Task ReportsDiagnostic_WhenSafeMethodPredicateHasUnrestrictedOrBranch()
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
                                args.Outcome.Result?.RequestMessage?.Method == HttpMethod.Get || true;
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
    public async Task DoesNotReport_WhenRetryPredicateAllowsOnlyMultipleSafeMethods()
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
                                args.Outcome.Result?.RequestMessage?.Method == HttpMethod.Get ||
                                args.Outcome.Result?.RequestMessage?.Method == HttpMethod.Head;
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
    public async Task ReportsDiagnostic_WhenSafeRetryPredicateUsesCustomShouldHandleProperty()
    {
        const string source = """
            using System;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;
            using CustomRetry;

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

            namespace CustomRetry
            {
                public sealed class RetryOptions
                {
                    public Func<RetryPredicateArguments, bool>? ShouldHandle { get; set; }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR041, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenRetryPredicateUsesCustomHttpMethodLookalike()
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
                            options.Retry.ShouldHandle = args => args.Method == HttpMethod.Get;
                        });
                }
            }

            public sealed class PaymentsClient(System.Net.Http.HttpClient httpClient)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return httpClient.PostAsync("/payments", null, cancellationToken);
                }
            }

            public static class HttpMethod
            {
                public static object Get { get; } = new();
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
                public object Method { get; } = new();
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
    public async Task ReportsDiagnostic_WhenTypedClientSendsNullForgivingUnsafeHttpRequestMessage()
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
                    return httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Post!, "/payments")!, cancellationToken);
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
    public async Task ReportsDiagnostic_WhenTypedClientSendsConnectHttpRequestMessage()
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
                        .AddHttpClient<TunnelClient>()
                        .AddStandardResilienceHandler();
                }
            }

            public sealed class TunnelClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> ConnectAsync(CancellationToken cancellationToken)
                {
                    var request = new HttpRequestMessage(HttpMethod.Connect, "/tunnel");
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
    public async Task ReportsDiagnostic_WhenTypedClientSendsUnsafeHttpRequestMessageWithConstantMethod()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class MethodNames
            {
                public const string Post = "POST";
            }

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
                    var request = new HttpRequestMessage(new HttpMethod(MethodNames.Post), "/payments");
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
    public async Task DoesNotReport_WhenTypedClientRequestLocalIsReassignedBeforeSend()
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
                    var request = new HttpRequestMessage(HttpMethod.Post, "/payments");
                    request = new HttpRequestMessage(HttpMethod.Get, "/payments");
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
    public async Task ReportsDiagnostic_WhenTypedClientRequestLocalKeepsUnsafeMethodUntilSend()
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
    public async Task ReportsDiagnostic_WhenTypedClientSendsNullForgivingUnsafeHttpRequestMessageInitializer()
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
                    return httpClient.SendAsync(new HttpRequestMessage { Method = HttpMethod.Delete! }, cancellationToken);
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
    public async Task DoesNotReport_WhenTypedClientSendsSafeHttpRequestMessageWithConstantMethod()
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
                    const string get = "GET";
                    var request = new HttpRequestMessage(new HttpMethod(get), "/payments");
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
    public async Task ReportsDiagnostic_WhenNamedClientStandardResilienceHandlerIsSplitAcrossBuilderLocal()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    var builder = services.AddHttpClient("payments");
                    builder.AddStandardResilienceHandler();
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
    public async Task DoesNotReport_WhenNamedClientBuilderLocalIsReassignedBeforeStandardHandler()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    var builder = services.AddHttpClient("payments");
                    builder = services.AddHttpClient("catalog");
                    builder.AddStandardResilienceHandler();
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
    public async Task ReportsDiagnostic_WhenNamedClientFactoryFieldIsThisQualified()
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

            public sealed class PaymentJob
            {
                private readonly IHttpClientFactory _factory;

                public PaymentJob(IHttpClientFactory factory)
                {
                    _factory = factory;
                }

                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    var client = this._factory.CreateClient("payments");
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
    public async Task ReportsDiagnostic_WhenNamedClientNameUsesConstant()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class ClientNames
            {
                public const string Payments = "payments";
            }

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient(ClientNames.Payments)
                        .AddStandardResilienceHandler();
                }
            }

            public sealed class PaymentJob(IHttpClientFactory factory)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    var client = factory.CreateClient(ClientNames.Payments);
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
    public async Task DoesNotReport_WhenNamedClientConstantsAreDifferent()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class ClientNames
            {
                public const string Catalog = "catalog";
                public const string Payments = "payments";
            }

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient(ClientNames.Catalog)
                        .AddStandardResilienceHandler();
                }
            }

            public sealed class PaymentJob(IHttpClientFactory factory)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    var client = factory.CreateClient(ClientNames.Payments);
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
    public async Task DoesNotReport_WhenNamedClientCallUsesCustomFactory()
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

            public sealed class PaymentJob(CustomFactory factory)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    var client = factory.CreateClient("payments");
                    return client.PostAsync("/payments", null, cancellationToken);
                }
            }

            public sealed class CustomFactory
            {
                public Custom.HttpClient CreateClient(string name) => new();
            }

            namespace Custom
            {
                public sealed class HttpClient
                {
                    public Task<HttpResponseMessage> PostAsync(string route, HttpContent? content, CancellationToken cancellationToken)
                    {
                        return Task.FromResult(new HttpResponseMessage());
                    }
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

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
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
    public async Task DoesNotReport_WhenNamedClientLocalIsReassignedBeforeUnsafeCall()
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
                    client = new HttpClient();
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
    public async Task DoesNotReport_WhenLookalikeNamedClientRegistrationIsNotIServiceCollection()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static CustomBuilder Configure(CustomServices services)
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

            public sealed class CustomServices
            {
            }

            public sealed class CustomBuilder
            {
            }

            public static class CustomBuilderExtensions
            {
                public static CustomBuilder AddHttpClient(this CustomServices services, string name) => new();
                public static CustomBuilder AddStandardResilienceHandler(this CustomBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task CodeFix_AddsDisableForUnsafeHttpMethodsConfiguration()
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
                public static IHttpClientBuilder AddStandardResilienceHandler(
                    this IHttpClientBuilder builder,
                    Action<HttpStandardResilienceOptions> configure)
                {
                    configure(new HttpStandardResilienceOptions());
                    return builder;
                }
            }

            public sealed class HttpStandardResilienceOptions
            {
                public RetryOptions Retry { get; } = new();
            }

            public sealed class RetryOptions
            {
                public void DisableForUnsafeHttpMethods()
                {
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR041_UnsafeMethodRetryAnalyzer, HCR041_DisableUnsafeMethodRetriesCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(".AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods())", fixedSource);
    }
}
