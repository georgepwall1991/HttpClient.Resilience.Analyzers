using HttpClient.Resilience.Analyzers.Analyzers.Resilience;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Models;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.Resilience;

/// <summary>
/// Explicit mutant-killer tests. Each case is chosen so flipping a boolean, swapping a
/// method name, or widening a namespace gate would fail.
/// </summary>
public sealed class HCR042_UnsafeMethodHedgingMutationTests
{
    [Theory]
    [InlineData("Connect", true)]
    [InlineData("Delete", true)]
    [InlineData("Patch", true)]
    [InlineData("Post", true)]
    [InlineData("Put", true)]
    [InlineData("Get", false)]
    [InlineData("Head", false)]
    [InlineData("Options", false)]
    [InlineData("Trace", false)]
    [InlineData("Posting", false)]
    public void HttpMethodSafety_ClassifiesMethodNames(string methodName, bool unsafeExpected)
    {
        Assert.Equal(unsafeExpected, HttpMethodSafety.IsUnsafeHttpMethodName(methodName, ignoreCase: false));
        Assert.Equal(
            methodName is "Get" or "Head" or "Options" or "Trace",
            HttpMethodSafety.IsSafeHttpMethodName(methodName));
    }

    [Theory]
    [InlineData("post", true)]
    [InlineData("POST", true)]
    [InlineData("get", false)]
    public void HttpMethodSafety_CustomHttpMethodStringsAreCaseInsensitive(string methodName, bool unsafeExpected)
    {
        Assert.Equal(unsafeExpected, HttpMethodSafety.IsUnsafeHttpMethodName(methodName, ignoreCase: true));
        Assert.False(HttpMethodSafety.IsUnsafeHttpMethodName(methodName, ignoreCase: false));
    }

    [Fact]
    public void UnsafeAndSafeMethodSets_AreExactRfcSets()
    {
        Assert.Equal(new[] { "Connect", "Delete", "Patch", "Post", "Put" }, HttpMethodSafety.UnsafeHttpMethodNames);
        Assert.Equal(new[] { "Connect", "Delete", "Patch", "Post", "Put" }, HttpMethodSafety.UnsafeHttpMethodPrefixes);
        Assert.Equal(new[] { "Get", "Head", "Options", "Trace" }, HttpMethodSafety.SafeHttpMethodNames);
    }

    [Fact]
    public void HttpMethodSafety_PrefixMatchDoesNotTreatGetAsUnsafe()
    {
        Assert.False(HttpMethodSafety.MethodNameStartsWithUnsafePrefix("GetAsync"));
        Assert.False(HttpMethodSafety.MethodNameStartsWithUnsafePrefix("GetStringAsync"));
        Assert.True(HttpMethodSafety.MethodNameStartsWithUnsafePrefix("PostAsJsonAsync"));
        Assert.True(HttpMethodSafety.MethodNameStartsWithUnsafePrefix("DeleteFromJsonAsync"));
        Assert.True(HttpMethodSafety.MethodNameStartsWithUnsafePrefix("Posting"));
    }

    [Fact]
    public async Task DoesNotReport_WhenMethodNameOnlyContainsUnsafeTokenAfterSafePrefix()
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
                    return httpClient.GetAsync("/payments", cancellationToken);
                }
            }

            public interface IServiceCollection { }
            public interface IHttpClientBuilder { }
            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services) => null!;
                public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        Assert.Empty(await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source));
    }

    [Fact]
    public async Task ReportsHcr042AndNotHcr041_ForHedgingHandler()
    {
        var source = HedgingSources.TypedClient("""httpClient.PostAsync("/payments", null, cancellationToken)""");

        var hedging = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);
        var retry = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(hedging).Id);
        Assert.Empty(retry);
    }

    [Fact]
    public async Task ReportsHcr041AndNotHcr042_ForResilienceHandler()
    {
        var source = HedgingSources.TypedClient(
            """httpClient.PostAsync("/payments", null, cancellationToken)""",
            handlerCall: ".AddStandardResilienceHandler()");

        var hedging = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);
        var retry = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(hedging);
        Assert.Equal(DiagnosticIds.HCR041, Assert.Single(retry).Id);
    }
}
