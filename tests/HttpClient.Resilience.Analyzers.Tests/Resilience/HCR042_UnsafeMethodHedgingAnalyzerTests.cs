using HttpClient.Resilience.Analyzers.Analyzers.Resilience;
using HttpClient.Resilience.Analyzers.CodeFixes;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.Resilience;

public sealed class HCR042_UnsafeMethodHedgingAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenStandardHedgingHandlerIsUsedWithUnsafeTypedClientCall()
    {
        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>
            .GetDiagnosticsAsync(HedgingSources.TypedClient(httpCall: """httpClient.PostAsync("/payments", null, cancellationToken)"""));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR042, diagnostic.Id);
        Assert.Equal(
            "Standard hedging replays unsafe HTTP methods concurrently. Do not hedge POST/PUT/PATCH/DELETE/CONNECT unless the operation is idempotent.",
            diagnostic.GetMessage());
        Assert.Equal(
            "AddStandardHedgingHandler",
            diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public async Task ReportsSingleDiagnostic_WhenTypedClientRegistrationAlsoHasNamedClientName()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services.AddHttpClient<PaymentsClient>("payments").AddStandardHedgingHandler();
                }
            }

            public sealed class PaymentsClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return httpClient.PostAsync("/payments", null, cancellationToken);
                }
            }

            public sealed class PaymentsJob(IHttpClientFactory factory)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    var client = factory.CreateClient("payments");
                    return client.PostAsync("/payments", null, cancellationToken);
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services, string name) => null!;
                public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(diagnostics).Id);
    }

    [Theory]
    [InlineData("PostAsync(\"/payments\", null, cancellationToken)")]
    [InlineData("PutAsync(\"/payments\", null, cancellationToken)")]
    [InlineData("PatchAsync(\"/payments\", null, cancellationToken)")]
    [InlineData("DeleteAsync(\"/payments\", cancellationToken)")]
    public async Task ReportsDiagnostic_WhenTypedClientSendsUnsafeMethod(string httpCall)
    {
        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>
            .GetDiagnosticsAsync(HedgingSources.TypedClient(httpCall: "httpClient." + httpCall));

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(diagnostics).Id);
    }

    [Theory]
    [InlineData("GetAsync(\"/catalog\", cancellationToken)")]
    public async Task DoesNotReport_WhenTypedClientOnlySendsSafeHttpMethods(string httpCall)
    {
        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>
            .GetDiagnosticsAsync(HedgingSources.TypedClient(httpCall: "httpClient." + httpCall));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenHandlerIsStandardResilienceInsteadOfHedging()
    {
        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>
            .GetDiagnosticsAsync(HedgingSources.TypedClient(
                httpCall: """httpClient.PostAsync("/payments", null, cancellationToken)""",
                handlerCall: ".AddStandardResilienceHandler()"));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenStandardHedgingHandlerIsCustomExtension()
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
                        .AddStandardHedgingHandler();
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
                    public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder) => builder;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTypedClientSendsConnectHttpRequestMessage()
    {
        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>
            .GetDiagnosticsAsync(HedgingSources.TypedClient(
                httpCall: "httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Connect, \"/tunnel\"), cancellationToken)"));

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(diagnostics).Id);
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
                .AddStandardHedgingHandler();

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
                public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTypedClientHedgingHandlerIsSplitAcrossBuilderLocal()
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
                    builder.AddStandardHedgingHandler();
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
                public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenTypedClientBuilderLocalIsReassignedBeforeHedgingHandler()
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
                    builder.AddStandardHedgingHandler();
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
                public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

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
                    return services.AddHttpClient<PaymentsClient>().AddStandardHedgingHandler();
                }
            }

            public sealed class PaymentsClient
            {
                private readonly HttpClient _httpClient;

                public PaymentsClient(HttpClient httpClient)
                {
                    _httpClient = httpClient;
                }

                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return this._httpClient.PostAsync("/payments", null, cancellationToken);
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
                public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(diagnostics).Id);
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
                    return services.AddHttpClient<IPaymentsClient, PaymentsClient>().AddStandardHedgingHandler();
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
                public static IHttpClientBuilder AddHttpClient<TService, TImplementation>(this IServiceCollection services)
                    where TImplementation : class, TService => null!;
                public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(diagnostics).Id);
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
                    return services.AddHttpClient<PaymentsClient>().AddStandardHedgingHandler();
                }
            }

            public sealed class PaymentsClient(Collaborator collaborator)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return collaborator.PostAsync("/payments", cancellationToken);
                }
            }

            public sealed class Collaborator
            {
                public Task<HttpResponseMessage> PostAsync(string route, CancellationToken cancellationToken) => null!;
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
                public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenRetryDisableForUnsafeHttpMethodsIsPresentOnHedgingHandler()
    {
        // Retry's DisableForUnsafeHttpMethods() does not disable hedging, so HCR042 must still fire.
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
                        .AddStandardHedgingHandler(options => options.Retry.DisableForUnsafeHttpMethods());
                }
            }

            public sealed class PaymentsClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return httpClient.PostAsync("/payments", null, cancellationToken);
                }
            }

            public sealed class HttpStandardHedgingResilienceOptions
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
                public static IHttpClientBuilder AddStandardHedgingHandler(
                    this IHttpClientBuilder builder,
                    System.Action<HttpStandardHedgingResilienceOptions> configure) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenHedgingPredicateOnlyAllowsSafeMethods()
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
                        .AddStandardHedgingHandler(options =>
                        {
                            options.Hedging.ShouldHandle = args =>
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

            public sealed class HttpStandardHedgingResilienceOptions
            {
                public HedgingOptions Hedging { get; } = new();
            }

            public sealed class HedgingOptions
            {
                public Func<HedgingPredicateArguments, bool>? ShouldHandle { get; set; }
            }

            public sealed class HedgingPredicateArguments
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
                public static IHttpClientBuilder AddStandardHedgingHandler(
                    this IHttpClientBuilder builder,
                    Action<HttpStandardHedgingResilienceOptions> configure) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenHedgingPredicateUsesSafeHttpMethodEquals()
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
                        .AddStandardHedgingHandler(options =>
                        {
                            options.Hedging.ShouldHandle = args =>
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

            public sealed class HttpStandardHedgingResilienceOptions
            {
                public HedgingOptions Hedging { get; } = new();
            }

            public sealed class HedgingOptions
            {
                public Func<HedgingPredicateArguments, bool>? ShouldHandle { get; set; }
            }

            public sealed class HedgingPredicateArguments
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
                public static IHttpClientBuilder AddStandardHedgingHandler(
                    this IHttpClientBuilder builder,
                    Action<HttpStandardHedgingResilienceOptions> configure) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenHedgingPredicateUsesObjectEqualsWithSafeMethod()
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
                        .AddStandardHedgingHandler(options =>
                        {
                            options.Hedging.ShouldHandle = args =>
                                object.Equals(HttpMethod.Get, args.Outcome.Result?.RequestMessage?.Method);
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

            public sealed class HttpStandardHedgingResilienceOptions
            {
                public HedgingOptions Hedging { get; } = new();
            }

            public sealed class HedgingOptions
            {
                public Func<HedgingPredicateArguments, bool>? ShouldHandle { get; set; }
            }

            public sealed class HedgingPredicateArguments
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
                public static IHttpClientBuilder AddStandardHedgingHandler(
                    this IHttpClientBuilder builder,
                    Action<HttpStandardHedgingResilienceOptions> configure) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenObjectEqualsPredicateMixesSafeAndUnsafeMethods()
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
                        .AddStandardHedgingHandler(options =>
                        {
                            options.Hedging.ShouldHandle = args =>
                                object.Equals(HttpMethod.Get, HttpMethod.Post);
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

            public sealed class HttpStandardHedgingResilienceOptions
            {
                public HedgingOptions Hedging { get; } = new();
            }

            public sealed class HedgingOptions
            {
                public Func<HedgingPredicateArguments, bool>? ShouldHandle { get; set; }
            }

            public sealed class HedgingPredicateArguments
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
                public static IHttpClientBuilder AddStandardHedgingHandler(
                    this IHttpClientBuilder builder,
                    Action<HttpStandardHedgingResilienceOptions> configure) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenPollyHedgingShouldHandleAllowsOnlySafeMethods()
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
                        .AddStandardHedgingHandler(options =>
                        {
                            options.ShouldHandle = args =>
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

            namespace Polly.Hedging
            {
                public sealed class HttpStandardHedgingResilienceOptions
                {
                    public Func<HedgingPredicateArguments, bool>? ShouldHandle { get; set; }
                }

                public sealed class HedgingPredicateArguments
                {
                    public Outcome Outcome { get; } = new();
                }

                public sealed class Outcome
                {
                    public HttpResponseMessage? Result { get; set; }
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
                public static IHttpClientBuilder AddStandardHedgingHandler(
                    this IHttpClientBuilder builder,
                    Action<Polly.Hedging.HttpStandardHedgingResilienceOptions> configure) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenHedgingPredicateStillAllowsUnsafeMethod()
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
                        .AddStandardHedgingHandler(options =>
                        {
                            options.Hedging.ShouldHandle = args =>
                                args.Outcome.Result?.RequestMessage?.Method == HttpMethod.Get ||
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

            public sealed class HttpStandardHedgingResilienceOptions
            {
                public HedgingOptions Hedging { get; } = new();
            }

            public sealed class HedgingOptions
            {
                public Func<HedgingPredicateArguments, bool>? ShouldHandle { get; set; }
            }

            public sealed class HedgingPredicateArguments
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
                public static IHttpClientBuilder AddStandardHedgingHandler(
                    this IHttpClientBuilder builder,
                    Action<HttpStandardHedgingResilienceOptions> configure) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTypedClientSendsUnsafeHttpRequestMessage()
    {
        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>
            .GetDiagnosticsAsync(HedgingSources.TypedClient(
                httpCall: "httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Post, \"/payments\"), cancellationToken)"));

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(diagnostics).Id);
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
                    return services.AddHttpClient<PaymentsClient>().AddStandardHedgingHandler();
                }
            }

            public sealed class PaymentsClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
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
                public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

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
                    return services.AddHttpClient<PaymentsClient>().AddStandardHedgingHandler();
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
                public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder) => builder;
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

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>
            .GetDiagnosticsAsync(registrations, client);

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenNamedClientWithStandardHedgingSendsUnsafeMethod()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services.AddHttpClient("payments").AddStandardHedgingHandler();
                }
            }

            public sealed class PaymentsJob(IHttpClientFactory factory)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    var client = factory.CreateClient("payments");
                    return client.PostAsync("/payments", null, cancellationToken);
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name) => null!;
                public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(diagnostics).Id);
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
                    return services.AddHttpClient(ClientNames.Payments).AddStandardHedgingHandler();
                }
            }

            public sealed class PaymentsJob(IHttpClientFactory factory)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    var client = factory.CreateClient(ClientNames.Payments);
                    return client.PostAsync("/payments", null, cancellationToken);
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name) => null!;
                public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(diagnostics).Id);
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
                    return services.AddHttpClient("payments").AddStandardHedgingHandler();
                }
            }

            public sealed class CatalogJob(IHttpClientFactory factory)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    var client = factory.CreateClient("catalog");
                    return client.PostAsync("/catalog", null, cancellationToken);
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name) => null!;
                public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
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
                    return services.AddHttpClient("payments").AddStandardHedgingHandler();
                }
            }

            public sealed class PaymentsJob(IHttpClientFactory factory)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return factory.CreateClient("payments").PostAsync("/payments", null, cancellationToken);
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name) => null!;
                public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(diagnostics).Id);
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
                    return services.AddHttpClient("payments").AddStandardHedgingHandler();
                }
            }

            public sealed class PaymentsJob(IHttpClientFactory factory)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    var client = factory.CreateClient("payments");
                    return client.PostAsync("/payments", null, cancellationToken);
                }
            }

            public sealed class CustomServices
            {
            }

            public sealed class CustomBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class CustomBuilderExtensions
            {
                public static CustomBuilder AddHttpClient(this CustomServices services, string name) => new();
                public static CustomBuilder AddStandardHedgingHandler(this CustomBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task CodeFix_ReplacesHedgingWithStandardResilienceAndDisablesUnsafeRetries()
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
                        .AddStandardHedgingHandler();
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
                public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder) => builder;
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

        var fixedSource = await CodeFixVerifier<HCR042_UnsafeMethodHedgingAnalyzer, HCR042_ReplaceHedgingWithSafeResilienceCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            ".AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods())",
            fixedSource);
        Assert.DoesNotContain(".AddStandardHedgingHandler();", fixedSource);

        var titles = await CodeFixVerifier<HCR042_UnsafeMethodHedgingAnalyzer, HCR042_ReplaceHedgingWithSafeResilienceCodeFixProvider>
            .GetCodeFixTitlesAsync(source);
        Assert.Equal(HCR042_ReplaceHedgingWithSafeResilienceCodeFixProvider.Title, Assert.Single(titles));
    }

    [Fact]
    public async Task CodeFix_IsNotOffered_WhenExistingHedgingConfigurationMustBePreserved()
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
                        .AddStandardHedgingHandler(options =>
                        {
                            options.Hedging.ShouldHandle = args =>
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

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services) => null!;
                public static IHttpClientBuilder AddStandardHedgingHandler(
                    this IHttpClientBuilder builder,
                    Action<HttpStandardHedgingResilienceOptions> configure)
                {
                    configure(new HttpStandardHedgingResilienceOptions());
                    return builder;
                }
            }

            public sealed class HttpStandardHedgingResilienceOptions
            {
                public HedgingOptions Hedging { get; } = new();
            }

            public sealed class HedgingOptions
            {
                public Func<HedgingPredicateArguments, bool>? ShouldHandle { get; set; }
            }

            public sealed class HedgingPredicateArguments
            {
                public Outcome Outcome { get; } = new();
            }

            public sealed class Outcome
            {
                public HttpResponseMessage? Result { get; set; }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);
        Assert.Single(diagnostics);

        var titles = await CodeFixVerifier<HCR042_UnsafeMethodHedgingAnalyzer, HCR042_ReplaceHedgingWithSafeResilienceCodeFixProvider>
            .GetCodeFixTitlesAsync(source);

        Assert.Empty(titles);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenHedgingPredicateUsesSafeHttpMethodEqualsOnlyOnRetryProperty()
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
                        .AddStandardHedgingHandler(options =>
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

            public sealed class HttpStandardHedgingResilienceOptions
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
                public static IHttpClientBuilder AddStandardHedgingHandler(
                    this IHttpClientBuilder builder,
                    Action<HttpStandardHedgingResilienceOptions> configure) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(diagnostics).Id);
    }
}
