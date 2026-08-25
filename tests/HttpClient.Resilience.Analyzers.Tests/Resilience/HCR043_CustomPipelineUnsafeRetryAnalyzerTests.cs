using HttpClient.Resilience.Analyzers.Analyzers.Resilience;
using HttpClient.Resilience.Analyzers.CodeFixes;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;
using Microsoft.CodeAnalysis;

namespace HttpClient.Resilience.Analyzers.Tests.Resilience;

public sealed class HCR043_CustomPipelineUnsafeRetryAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenCustomPipelineRetriesUnsafeTypedClientPost()
    {
        var source = CustomPipelineSources.TypedClient();

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        AssertHcr043OnAddRetry(Assert.Single(diagnostics), source);
    }

    [Theory]
    [InlineData("""httpClient.PutAsync("/payments", null, cancellationToken)""")]
    [InlineData("""httpClient.PatchAsync("/payments", null, cancellationToken)""")]
    [InlineData("""httpClient.DeleteAsync("/payments", cancellationToken)""")]
    [InlineData("""httpClient.PostAsJsonAsync("/payments", new object(), cancellationToken)""")]
    public async Task ReportsDiagnostic_WhenTypedClientSendsOtherUnsafeMethods(string httpCall)
    {
        var extraTypes = httpCall.Contains("PostAsJsonAsync", StringComparison.Ordinal)
            ? """
                public static class HttpClientJsonExtensions
                {
                    public static Task<HttpResponseMessage> PostAsJsonAsync<T>(
                        this HttpClient client,
                        string requestUri,
                        T value,
                        CancellationToken cancellationToken) => null!;
                }
                """
            : "";

        var source = CustomPipelineSources.TypedClient(httpCall: httpCall, extraTypes: extraTypes);

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        AssertHcr043OnAddRetry(Assert.Single(diagnostics), source);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTypedClientSendsConnect()
    {
        var source = CustomPipelineSources.TypedClient(
            httpCall: """httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Connect, "/tunnel"), cancellationToken)""");

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        AssertHcr043OnAddRetry(Assert.Single(diagnostics), source);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTypedClientSendsUnsafeHttpRequestMessage()
    {
        var source = CustomPipelineSources.TypedClient(
            httpCall: """httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/payments"), cancellationToken)""");

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        AssertHcr043OnAddRetry(Assert.Single(diagnostics), source);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenNamedClientPipelineRetriesUnsafePost()
    {
        var source = CustomPipelineSources.NamedClient();

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        AssertHcr043OnAddRetry(Assert.Single(diagnostics), source);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenNamedClientNameUsesConstant()
    {
        var source = CustomPipelineSources.NamedClient(
            clientName: "ClientNames.Payments",
            createClientName: "ClientNames.Payments")
            .Replace(
                "public static class Registrations",
                """
                public static class ClientNames
                {
                    public const string Payments = "payments";
                }

                public static class Registrations
                """,
                StringComparison.Ordinal);

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        AssertHcr043OnAddRetry(Assert.Single(diagnostics), source);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenRetryIsChainedAfterTimeout()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            builder.AddTimeout(System.TimeSpan.FromSeconds(10)).AddRetry(new HttpRetryStrategyOptions());
                        }
                """);

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        AssertHcr043OnAddRetry(Assert.Single(diagnostics), source);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenConfigureCallbackHasBuilderAndContextParameters()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                (builder, context) =>
                        {
                            _ = context;
                            builder.AddRetry(new HttpRetryStrategyOptions());
                        }
                """);

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        AssertHcr043OnAddRetry(Assert.Single(diagnostics), source);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTypedClientHandlerIsSplitAcrossBuilderLocal()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;
            using Polly;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    var builder = services.AddHttpClient<PaymentsClient>();
                    builder.AddResilienceHandler("payments", pipeline =>
                    {
                        pipeline.AddRetry(new HttpRetryStrategyOptions());
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

            """ + CustomPipelineSources.FrameworkStubs;

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        AssertHcr043OnAddRetry(Assert.Single(diagnostics), source);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTwoGenericTypedClientImplementationSendsUnsafeMethod()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;
            using Polly;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<IPaymentsClient, PaymentsClient>()
                        .AddResilienceHandler("payments", builder =>
                        {
                            builder.AddRetry(new HttpRetryStrategyOptions());
                        });
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

            """ + CustomPipelineSources.FrameworkStubs;

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        AssertHcr043OnAddRetry(Assert.Single(diagnostics), source);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenConfigureCallbackIsLocalFunctionInSameMethod()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;
            using Polly;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<PaymentsClient>()
                        .AddResilienceHandler("payments", ConfigurePipeline);

                    static void ConfigurePipeline(ResiliencePipelineBuilder builder)
                    {
                        builder.AddRetry(new HttpRetryStrategyOptions());
                    }
                }
            }

            public sealed class PaymentsClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return httpClient.PostAsync("/payments", null, cancellationToken);
                }
            }

            """ + CustomPipelineSources.FrameworkStubs;

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        AssertHcr043OnAddRetry(Assert.Single(diagnostics), source);
    }

    [Fact]
    public async Task ReportsTwoDiagnostics_WhenCallbackContainsTwoAddRetryCalls()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            builder.AddRetry(new HttpRetryStrategyOptions());
                            builder.AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 3 });
                        }
                """);

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => AssertHcr043OnAddRetry(diagnostic, source));
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenMaxRetryAttemptsIsPositive()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            builder.AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 3 });
                        }
                """);

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        AssertHcr043OnAddRetry(Assert.Single(diagnostics), source);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenExpressionLambdaAddsRetry()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: "builder => builder.AddRetry(new HttpRetryStrategyOptions())");

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        AssertHcr043OnAddRetry(Assert.Single(diagnostics), source);
    }

    [Fact]
    public async Task DoesNotReport_WhenTypedClientOnlySendsSafeHttpMethods()
    {
        var source = CustomPipelineSources.TypedClient(
            httpCall: """httpClient.GetAsync("/catalog", cancellationToken)""");

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenPipelineOnlyAddsTimeout()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            builder.AddTimeout(System.TimeSpan.FromSeconds(10));
                        }
                """);

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenAddStandardResilienceHandlerIsUsedAlone()
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

            """ + CustomPipelineSources.FrameworkStubs;

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenOnlyHedgingHandlerIsRegistered()
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

            """ + CustomPipelineSources.FrameworkStubs;

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenAddRetryReceiverIsNotThePipelineBuilder()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            var other = new ResiliencePipelineBuilder();
                            other.AddRetry(new HttpRetryStrategyOptions());
                            builder.AddTimeout(System.TimeSpan.FromSeconds(10));
                        }
                """);

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenDisableForUnsafeHttpMethodsIsCalledOnOptionsLocal()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            var retryOptions = new HttpRetryStrategyOptions();
                            retryOptions.DisableForUnsafeHttpMethods();
                            builder.AddRetry(retryOptions);
                        }
                """);

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenDisableForUnsafeHttpMethodsIsCustomExtension()
    {
        var source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;
            using Polly;
            using CustomRetryGuards;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<PaymentsClient>()
                        .AddResilienceHandler("payments", builder =>
                        {
                            var retryOptions = new HttpRetryStrategyOptions();
                            retryOptions.DisableForUnsafeHttpMethods();
                            builder.AddRetry(retryOptions);
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

            public sealed class HttpRetryStrategyOptions
            {
            }

            namespace CustomRetryGuards
            {
                public static class RetryOptionsExtensions
                {
                    public static void DisableForUnsafeHttpMethods(this HttpRetryStrategyOptions options)
                    {
                    }
                }
            }

            """ + CustomPipelineSources.FrameworkStubs.Replace(
            "public sealed class HttpRetryStrategyOptions",
            "public sealed class HttpRetryStrategyOptionsUnused",
            StringComparison.Ordinal);

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        AssertHcr043OnAddRetry(Assert.Single(diagnostics), source);
    }

    [Fact]
    public async Task DoesNotReport_WhenRetryPredicateOnlyAllowsSafeMethodsOnOptionsLocal()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            var retryOptions = new HttpRetryStrategyOptions();
                            retryOptions.ShouldHandle = args =>
                                args.Outcome.Result?.RequestMessage?.Method == HttpMethod.Get;
                            builder.AddRetry(retryOptions);
                        }
                """);

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenRetryPredicateOnlyAllowsSafeMethodsInOptionsLocalInitializer()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            var retryOptions = new HttpRetryStrategyOptions
                            {
                                ShouldHandle = args =>
                                    args.Outcome.Result?.RequestMessage?.Method == HttpMethod.Get
                            };
                            builder.AddRetry(retryOptions);
                        }
                """);

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenRetryPredicateOnlyAllowsSafeMethodsInObjectInitializer()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            builder.AddRetry(new HttpRetryStrategyOptions
                            {
                                ShouldHandle = args =>
                                    args.Outcome.Result?.RequestMessage?.Method == HttpMethod.Get
                            });
                        }
                """);

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenRetryPredicateStillAllowsUnsafeMethod()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            builder.AddRetry(new HttpRetryStrategyOptions
                            {
                                ShouldHandle = args =>
                                    args.Outcome.Result?.RequestMessage?.Method == HttpMethod.Post
                            });
                        }
                """);

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        AssertHcr043OnAddRetry(Assert.Single(diagnostics), source);
    }

    [Fact]
    public async Task DoesNotReport_WhenMaxRetryAttemptsIsLiteralZeroOnOptionsLocalInitializer()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            var retryOptions = new HttpRetryStrategyOptions { MaxRetryAttempts = 0 };
                            builder.AddRetry(retryOptions);
                        }
                """);

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenMaxRetryAttemptsIsLiteralZero()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            builder.AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 0 });
                        }
                """);

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenMaxRetryAttemptsAssignedZeroOnOptionsLocal()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            var retryOptions = new HttpRetryStrategyOptions();
                            retryOptions.MaxRetryAttempts = 0;
                            builder.AddRetry(retryOptions);
                        }
                """);

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenMaxRetryAttemptsIsOne()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            builder.AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 1 });
                        }
                """);

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        AssertHcr043OnAddRetry(Assert.Single(diagnostics), source);
    }

    [Fact]
    public async Task DoesNotReport_WhenAddResilienceHandlerIsCustomExtension()
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
                        .AddResilienceHandler("payments", builder =>
                        {
                            builder.AddRetry(new HttpRetryStrategyOptions());
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

            public sealed class ResiliencePipelineBuilder
            {
            }

            public sealed class HttpRetryStrategyOptions
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
                    public static IHttpClientBuilder AddResilienceHandler(
                        this IHttpClientBuilder builder,
                        string name,
                        System.Action<ResiliencePipelineBuilder> configure) => builder;

                    public static ResiliencePipelineBuilder AddRetry(
                        this ResiliencePipelineBuilder builder,
                        HttpRetryStrategyOptions options) => builder;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenAddRetryIsCustomExtension()
    {
        const string source = """
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
                        .AddResilienceHandler("payments", builder =>
                        {
                            builder.AddRetry(new HttpRetryStrategyOptions());
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

            namespace CustomRetry
            {
                public static class CustomRetryExtensions
                {
                    public static ResiliencePipelineBuilder AddRetry(
                        this ResiliencePipelineBuilder builder,
                        HttpRetryStrategyOptions options) => builder;
                }
            }

            """ + CustomPipelineSources.FrameworkStubs;

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenConfigureCallbackIsMethodGroupOnAnotherType()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;
            using Polly;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<PaymentsClient>()
                        .AddResilienceHandler("payments", PipelineHelpers.Configure);
                }
            }

            public static class PipelineHelpers
            {
                public static void Configure(ResiliencePipelineBuilder builder)
                {
                    builder.AddRetry(new HttpRetryStrategyOptions());
                }
            }

            public sealed class PaymentsClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return httpClient.PostAsync("/payments", null, cancellationToken);
                }
            }

            """ + CustomPipelineSources.FrameworkStubs;

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsOnlyUnguardedRetry_WhenCallbackHasGuardedAndUnguardedAddRetry()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            var safe = new HttpRetryStrategyOptions();
                            safe.DisableForUnsafeHttpMethods();
                            builder.AddRetry(safe);
                            builder.AddRetry(new HttpRetryStrategyOptions());
                        }
                """);

        var diagnostics = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(source);

        AssertHcr043OnAddRetry(Assert.Single(diagnostics), source);
    }

    [Fact]
    public async Task CodeFix_IntroducesOptionsLocalAndDisableForUnsafeHttpMethods()
    {
        var source = CustomPipelineSources.TypedClient();

        var fixedSource = await CodeFixVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer, HCR043_DisableUnsafeMethodRetriesCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains("var retryOptions = new HttpRetryStrategyOptions();", fixedSource, StringComparison.Ordinal);
        Assert.Contains("retryOptions.DisableForUnsafeHttpMethods();", fixedSource, StringComparison.Ordinal);
        Assert.Contains("builder.AddRetry(retryOptions);", fixedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("builder.AddRetry(new HttpRetryStrategyOptions())", fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_PreservesObjectInitializer()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            builder.AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 3 });
                        }
                """);

        var fixedSource = await CodeFixVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer, HCR043_DisableUnsafeMethodRetriesCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains("var retryOptions = new HttpRetryStrategyOptions { MaxRetryAttempts = 3 };", fixedSource, StringComparison.Ordinal);
        Assert.Contains("retryOptions.DisableForUnsafeHttpMethods();", fixedSource, StringComparison.Ordinal);
        Assert.Contains("builder.AddRetry(retryOptions);", fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_ReusesExistingOptionsVariable()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            var retryOptions = new HttpRetryStrategyOptions { MaxRetryAttempts = 3 };
                            builder.AddRetry(retryOptions);
                        }
                """);

        var fixedSource = await CodeFixVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer, HCR043_DisableUnsafeMethodRetriesCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains("retryOptions.DisableForUnsafeHttpMethods();", fixedSource, StringComparison.Ordinal);
        Assert.Contains("builder.AddRetry(retryOptions);", fixedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("var retryOptions2 =", fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_IsNotOffered_WhenOptionsVariableAlreadyHasGuardAttempt()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            var retryOptions = new HttpRetryStrategyOptions { MaxRetryAttempts = 3 };
                            retryOptions.DisableForUnknownStrategy();
                            builder.AddRetry(retryOptions);
                        }
                """,
            extraTypes: """

                public static class RetryOptionExtensions
                {
                    public static void DisableForUnknownStrategy(this HttpRetryStrategyOptions options)
                    {
                    }
                }
                """);

        var titles = await CodeFixVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer, HCR043_DisableUnsafeMethodRetriesCodeFixProvider>
            .GetCodeFixTitlesAsync(source);

        Assert.Empty(titles);
    }

    [Fact]
    public async Task CodeFix_ConvertsExpressionLambdaToBlock()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: "builder => builder.AddRetry(new HttpRetryStrategyOptions())");

        var fixedSource = await CodeFixVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer, HCR043_DisableUnsafeMethodRetriesCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains("retryOptions.DisableForUnsafeHttpMethods();", fixedSource, StringComparison.Ordinal);
        Assert.Contains("builder.AddRetry(retryOptions)", fixedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("builder => builder.AddRetry(new HttpRetryStrategyOptions())", fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_UsesUniqueLocalNameWhenRetryOptionsAlreadyExists()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            var retryOptions = 1;
                            _ = retryOptions;
                            builder.AddRetry(new HttpRetryStrategyOptions());
                        }
                """);

        var fixedSource = await CodeFixVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer, HCR043_DisableUnsafeMethodRetriesCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains("var retryOptions2 = new HttpRetryStrategyOptions();", fixedSource, StringComparison.Ordinal);
        Assert.Contains("retryOptions2.DisableForUnsafeHttpMethods();", fixedSource, StringComparison.Ordinal);
        Assert.Contains("builder.AddRetry(retryOptions2);", fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_IsNotOffered_WhenArgumentIsGenericRetryStrategyOptions()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>());
                        }
                """);

        var titles = await CodeFixVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer, HCR043_DisableUnsafeMethodRetriesCodeFixProvider>
            .GetCodeFixTitlesAsync(source);

        Assert.Empty(titles);
    }

    [Fact]
    public async Task CodeFix_TitleDescribesDisableUnsafeRetries()
    {
        var source = CustomPipelineSources.TypedClient();

        var titles = await CodeFixVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer, HCR043_DisableUnsafeMethodRetriesCodeFixProvider>
            .GetCodeFixTitlesAsync(source);

        Assert.Equal(HCR043_DisableUnsafeMethodRetriesCodeFixProvider.Title, Assert.Single(titles));
    }

    [Fact]
    public void CodeFix_UsesBatchFixAllProvider()
    {
        var provider = new HCR043_DisableUnsafeMethodRetriesCodeFixProvider();

        Assert.Same(
            Microsoft.CodeAnalysis.CodeFixes.WellKnownFixAllProviders.BatchFixer,
            provider.GetFixAllProvider());
    }

    [Fact]
    public async Task CodeFix_CanBeAppliedSequentiallyToTwoAddRetryCalls()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            builder.AddRetry(new HttpRetryStrategyOptions());
                            builder.AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 3 });
                        }
                """);

        var afterFirst = await CodeFixVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer, HCR043_DisableUnsafeMethodRetriesCodeFixProvider>
            .ApplyFirstCodeFixAllowingRemainingAsync(source);

        var remaining = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>
            .GetDiagnosticsAsync(afterFirst);
        Assert.Equal(DiagnosticIds.HCR043, Assert.Single(remaining).Id);

        var afterSecond = await CodeFixVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer, HCR043_DisableUnsafeMethodRetriesCodeFixProvider>
            .ApplyFirstCodeFixAsync(afterFirst);

        Assert.Empty(await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>.GetDiagnosticsAsync(afterSecond));
        Assert.Contains("retryOptions.DisableForUnsafeHttpMethods();", afterSecond, StringComparison.Ordinal);
        Assert.Contains("retryOptions2.DisableForUnsafeHttpMethods();", afterSecond, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_FixAllInDocumentFixesTwoAddRetryCalls()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            builder.AddRetry(new HttpRetryStrategyOptions());
                            builder.AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 3 });
                        }
                """);

        var fixedSource = await CodeFixVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer, HCR043_DisableUnsafeMethodRetriesCodeFixProvider>
            .ApplyFixAllInDocumentAsync(source);

        Assert.Empty(await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>.GetDiagnosticsAsync(fixedSource));
        Assert.Contains("retryOptions.DisableForUnsafeHttpMethods();", fixedSource, StringComparison.Ordinal);
        Assert.Contains("retryOptions2.DisableForUnsafeHttpMethods();", fixedSource, StringComparison.Ordinal);
        Assert.Contains("builder.AddRetry(retryOptions);", fixedSource, StringComparison.Ordinal);
        Assert.Contains("builder.AddRetry(retryOptions2);", fixedSource, StringComparison.Ordinal);
        Assert.Contains("new HttpRetryStrategyOptions { MaxRetryAttempts = 3 }", fixedSource, StringComparison.Ordinal);
    }

    private static void AssertHcr043OnAddRetry(Diagnostic diagnostic, string source)
    {
        Assert.Equal(DiagnosticIds.HCR043, diagnostic.Id);
        Assert.Equal(
            "AddRetry",
            source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
    }
}
