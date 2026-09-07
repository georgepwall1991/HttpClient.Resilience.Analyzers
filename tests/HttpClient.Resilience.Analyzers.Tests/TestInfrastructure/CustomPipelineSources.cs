namespace HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

internal static class CustomPipelineSources
{
    public const string FrameworkStubs = """
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

        public sealed class ResiliencePipelineBuilder
        {
        }

        public sealed class ResilienceHandlerContext
        {
        }

        public sealed class HttpRetryStrategyOptions
        {
            public int MaxRetryAttempts { get; set; }

            public System.Func<RetryPredicateArguments, bool>? ShouldHandle { get; set; }

            public void DisableForUnsafeHttpMethods()
            {
            }
        }

        public sealed class RetryStrategyOptions<T>
        {
            public int MaxRetryAttempts { get; set; }

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

        public static class ServiceCollectionExtensions
        {
            public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services) => null!;

            public static IHttpClientBuilder AddHttpClient<TService, TImplementation>(this IServiceCollection services) => null!;

            public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name) => null!;

            public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder) => builder;

            public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder) => builder;

            public static IHttpClientBuilder AddResilienceHandler(
                this IHttpClientBuilder builder,
                string name,
                System.Action<ResiliencePipelineBuilder> configure) => builder;

            public static IHttpClientBuilder AddResilienceHandler(
                this IHttpClientBuilder builder,
                string name,
                System.Action<ResiliencePipelineBuilder, ResilienceHandlerContext> configure) => builder;
        }

        namespace Polly
        {
            public static class RetryResiliencePipelineBuilderExtensions
            {
                public static ResiliencePipelineBuilder AddRetry(
                    this ResiliencePipelineBuilder builder,
                    HttpRetryStrategyOptions options) => builder;

                public static ResiliencePipelineBuilder AddRetry(this ResiliencePipelineBuilder builder) => builder;

                public static ResiliencePipelineBuilder AddRetry<T>(
                    this ResiliencePipelineBuilder builder,
                    RetryStrategyOptions<T> options) => builder;

                public static ResiliencePipelineBuilder AddTimeout(
                    this ResiliencePipelineBuilder builder,
                    System.TimeSpan timeout) => builder;

                public static ResiliencePipelineBuilder AddCircuitBreaker(
                    this ResiliencePipelineBuilder builder,
                    object options) => builder;
            }
        }

        namespace Microsoft.Extensions.Http.Resilience
        {
            public static class HttpRetryStrategyOptionsExtensions
            {
                public static void DisableForUnsafeHttpMethods(this global::HttpRetryStrategyOptions options)
                {
                }
            }
        }
        """;

    public static string TypedClient(
        string httpCall = """httpClient.PostAsync("/payments", null, cancellationToken)""",
        string pipelineConfigure = """
            builder =>
                    {
                        builder.AddRetry(new HttpRetryStrategyOptions());
                    }
            """,
        string extraUsings = "",
        string extraTypes = "")
    {
        return $$"""
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;
            using Polly;
            {{extraUsings}}
            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<PaymentsClient>()
                        .AddResilienceHandler("payments", {{pipelineConfigure}});
                }
            }

            public sealed class PaymentsClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return {{httpCall}};
                }
            }

            {{FrameworkStubs}}
            {{extraTypes}}
            """;
    }

    public static string NamedClient(
        string httpCall = """client.PostAsync("/payments", null, cancellationToken)""",
        string pipelineConfigure = """
            builder =>
                    {
                        builder.AddRetry(new HttpRetryStrategyOptions());
                    }
            """,
        string clientName = "\"payments\"",
        string createClientName = "\"payments\"")
    {
        return $$"""
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;
            using Polly;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient({{clientName}})
                        .AddResilienceHandler("payments", {{pipelineConfigure}});
                }
            }

            public sealed class PaymentJob(IHttpClientFactory factory)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    var client = factory.CreateClient({{createClientName}});
                    return {{httpCall}};
                }
            }

            {{FrameworkStubs}}
            """;
    }
}
