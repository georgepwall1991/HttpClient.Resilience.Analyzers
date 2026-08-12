namespace HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

internal static class HedgingSources
{
    public static string TypedClient(
        string httpCall,
        string handlerCall = ".AddStandardHedgingHandler()",
        string extraTypes = "")
    {
        return $$"""
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<PaymentsClient>()
                        {{handlerCall}};
                }
            }

            public sealed class PaymentsClient(HttpClient httpClient)
            {
                public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return {{httpCall}};
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
                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder) => builder;
            }

            {{extraTypes}}
            """;
    }

    public static string TypedClientWithHedgingPredicate(string predicateExpression)
    {
        return TypedClient(
            """httpClient.PostAsync("/payments", null, cancellationToken)""",
            handlerCall: $$"""
                .AddStandardHedgingHandler(options =>
                {
                    options.Hedging.ShouldHandle = args => {{predicateExpression}};
                })
                """,
            extraTypes: """
                public sealed class HttpStandardHedgingResilienceOptions
                {
                    public HedgingOptions Hedging { get; } = new();
                }

                public sealed class HedgingOptions
                {
                    public System.Func<HedgingPredicateArguments, bool>? ShouldHandle { get; set; }
                }

                public sealed class HedgingPredicateArguments
                {
                    public Outcome Outcome { get; } = new();
                }

                public sealed class Outcome
                {
                    public System.Net.Http.HttpResponseMessage? Result { get; set; }
                }

                public static class HedgingOptionExtensions
                {
                    public static IHttpClientBuilder AddStandardHedgingHandler(
                        this IHttpClientBuilder builder,
                        System.Action<HttpStandardHedgingResilienceOptions> configure) => builder;
                }
                """);
    }
}
