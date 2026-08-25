using HttpClient.Resilience.Analyzers.Analyzers.Resilience;
using HttpClient.Resilience.Analyzers.CodeFixes;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;
using Xunit;

namespace HttpClient.Resilience.Analyzers.Tests.Resilience;

public sealed class HCR041_ConfiguredHandlerCodeFixTests
{
    private const string Framework = """
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
                        options.Retry.MaxRetryAttempts = 5;
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
            public int MaxRetryAttempts { get; set; }

            public void DisableForUnsafeHttpMethods()
            {
            }

            public System.Func<RetryPredicateArguments, bool>? ShouldHandle { get; set; }
        }

        public sealed class RetryPredicateArguments
        {
            public Outcome Outcome { get; } = new();
        }

        public sealed class Outcome
        {
            public HttpResponseMessage? Result { get; set; }
        }
        """;

    [Fact]
    public async Task CodeFix_InjectsGuardIntoConfiguredLambda()
    {
        var diagnosticsBefore = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>
            .GetDiagnosticsAsync(Framework);
        Assert.Single(diagnosticsBefore);

        var fixedSource = await CodeFixVerifier<HCR041_UnsafeMethodRetryAnalyzer, HCR041_DisableUnsafeMethodRetriesCodeFixProvider>
            .ApplyFirstCodeFixAsync(Framework);

        Assert.Contains("options.Retry.DisableForUnsafeHttpMethods();", fixedSource, System.StringComparison.Ordinal);
        Assert.Contains("options.Retry.MaxRetryAttempts = 5;", fixedSource, System.StringComparison.Ordinal);
        Assert.Empty(await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(fixedSource));
    }

    [Fact]
    public async Task CodeFix_IsNotOffered_WhenConfiguredLambdaSetsShouldHandle()
    {
        var source = Framework.Replace(
            "options.Retry.MaxRetryAttempts = 5;",
            "options.Retry.ShouldHandle = args => args.Outcome.Result is not null;",
            System.StringComparison.Ordinal);

        var titles = await CodeFixVerifier<HCR041_UnsafeMethodRetryAnalyzer, HCR041_DisableUnsafeMethodRetriesCodeFixProvider>
            .GetCodeFixTitlesAsync(source);

        Assert.Empty(titles);
    }

    [Fact]
    public async Task CodeFix_ConvertsExpressionBodiedConfigureLambdaToBlock()
    {
        var source = Framework.Replace(
            "options =>\n                    {\n                        options.Retry.MaxRetryAttempts = 5;\n                    }",
            "options => options.Retry.MaxRetryAttempts = 5",
            System.StringComparison.Ordinal);

        var fixedSource = await CodeFixVerifier<HCR041_UnsafeMethodRetryAnalyzer, HCR041_DisableUnsafeMethodRetriesCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains("options.Retry.DisableForUnsafeHttpMethods();", fixedSource, System.StringComparison.Ordinal);
        Assert.Contains("options.Retry.MaxRetryAttempts = 5;", fixedSource, System.StringComparison.Ordinal);
        Assert.Empty(await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(fixedSource));
    }

    [Fact]
    public async Task CodeFix_FixAllHandlesMixedParameterlessAndConfiguredHandlers()
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
                        .AddStandardResilienceHandler()
                        .AddStandardResilienceHandler(options =>
                        {
                            options.Retry.MaxRetryAttempts = 5;
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
                public int MaxRetryAttempts { get; set; }

                public void DisableForUnsafeHttpMethods()
                {
                }
            }
            """;

        var diagnosticsBefore = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);
        Assert.Equal(2, diagnosticsBefore.Length);

        var fixedSource = await CodeFixVerifier<HCR041_UnsafeMethodRetryAnalyzer, HCR041_DisableUnsafeMethodRetriesCodeFixProvider>
            .ApplyFixAllInDocumentAsync(source);

        Assert.Empty(await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(fixedSource));
        Assert.Contains(".AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods())", fixedSource, System.StringComparison.Ordinal);
        Assert.Contains("options.Retry.DisableForUnsafeHttpMethods();", fixedSource, System.StringComparison.Ordinal);
    }



}
