using HttpClient.Resilience.Analyzers.Analyzers.Resilience;
using HttpClient.Resilience.Analyzers.CodeFixes;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Models;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

    [Theory]
    [InlineData("HttpMethod.Get == HttpMethod.Get")]
    [InlineData("HttpMethod.Get.Equals(HttpMethod.Get)")]
    [InlineData("object.Equals(HttpMethod.Get, HttpMethod.Get)")]
    [InlineData("1 == 1")]
    public async Task ReportsDiagnostic_WhenHedgingPredicateDoesNotInspectTheRequestMethod(string predicate)
    {
        var source = HedgingSources.TypedClientWithHedgingPredicate(predicate);

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenFrameworkHedgingExtensionLivesInDependencyInjectionNamespace()
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
                        return services.AddHttpClient<PaymentsClient>().AddStandardHedgingHandler();
                    }
                }

                public sealed class PaymentsClient(HttpClient httpClient)
                {
                    public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                    {
                        return httpClient.PostAsync("/payments", null, cancellationToken);
                    }
                }

                public interface IServiceCollection { }
                public interface IHttpClientBuilder { }
            }

            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ServiceCollectionExtensions
                {
                    public static Clients.IHttpClientBuilder AddHttpClient<TClient>(this Clients.IServiceCollection services) => null!;
                    public static Clients.IHttpClientBuilder AddStandardHedgingHandler(this Clients.IHttpClientBuilder builder) => builder;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenHedgingHandlerSymbolDoesNotBind()
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
                    return httpClient.PostAsync("/payments", null, cancellationToken);
                }
            }

            public interface IServiceCollection { }
            public interface IHttpClientBuilder { }
            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services) => null!;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenHedgingHandlerCandidatesIncludeALookalike()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using CustomResilience;

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
                    return httpClient.PostAsync("/payments", null, cancellationToken);
                }
            }

            public interface IServiceCollection { }
            public interface IHttpClientBuilder { }

            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class FrameworkExtensions
                {
                    public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services) => null!;
                    public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder) => builder;
                }
            }

            namespace CustomResilience
            {
                public static class CustomExtensions
                {
                    public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder) => builder;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void CodeFix_UsesBatchFixAllProvider()
    {
        var provider = new HCR042_ReplaceHedgingWithSafeResilienceCodeFixProvider();

        Assert.Same(WellKnownFixAllProviders.BatchFixer, provider.GetFixAllProvider());
    }

    [Fact]
    public async Task DoesNotReport_WhenSafePredicateIsAndedWithAnExtraCondition()
    {
        var source = HedgingSources.TypedClientWithHedgingPredicate(
            "args.Outcome.Result?.RequestMessage?.Method == HttpMethod.Get && args.Outcome.Result != null");

        Assert.Empty(await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source));
    }

    [Fact]
    public async Task DoesNotReport_WhenSafePredicateAllowsGetOrHead()
    {
        var source = HedgingSources.TypedClientWithHedgingPredicate(
            "args.Outcome.Result?.RequestMessage?.Method == HttpMethod.Get || args.Outcome.Result?.RequestMessage?.Method == HttpMethod.Head");

        Assert.Empty(await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source));
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTwoArgumentEqualsIsNotObjectEquals()
    {
        var source = HedgingSources.TypedClientWithHedgingPredicate(
            "System.Collections.Generic.EqualityComparer<object>.Default.Equals(HttpMethod.Get, args.Outcome.Result?.RequestMessage?.Method)");

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenPredicateUsesLookalikeHttpMethodType()
    {
        const string source = """
            using System;
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
                            options.Hedging.ShouldHandle = args => args.Method == HttpMethod.Get;
                        });
                }
            }

            public sealed class PaymentsClient(System.Net.Http.HttpClient httpClient)
            {
                public Task<System.Net.Http.HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
                {
                    return httpClient.PostAsync("/payments", null, cancellationToken);
                }
            }

            public static class HttpMethod
            {
                public static object Get { get; } = new();
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
                public object Method { get; } = new();
            }

            public interface IServiceCollection { }
            public interface IHttpClientBuilder { }
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
    public async Task ReportsDiagnostic_WhenAllHedgingHandlerCandidatesAreFrameworkButAmbiguous()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;

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
                    return httpClient.PostAsync("/payments", null, cancellationToken);
                }
            }

            public interface IServiceCollection { }
            public interface IHttpClientBuilder { }

            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class FrameworkExtensions
                {
                    public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services) => null!;
                    public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder) => builder;
                }
            }

            public static class GlobalHedgingExtensions
            {
                public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenUnresolvedShouldHandleCannotBeProvenSafe()
    {
        var source = HedgingSources.TypedClientWithHedgingPredicate(
            "args.Outcome.Result?.RequestMessage?.Method == HttpMethod.Get");
        source = source.Replace(
            "public System.Func<HedgingPredicateArguments, bool>? ShouldHandle { get; set; }",
            "");

        var diagnostics = await AnalyzerVerifier<HCR042_UnsafeMethodHedgingAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Equal(DiagnosticIds.HCR042, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public void FrameworkExtension_UsesReducedFromNamespace()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;

            namespace Clients
            {
                public static class Registrations
                {
                    public static IHttpClientBuilder Configure(IHttpClientBuilder builder)
                    {
                        return builder.AddStandardHedgingHandler();
                    }
                }

                public interface IServiceCollection { }
                public interface IHttpClientBuilder { }
            }

            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class ServiceCollectionExtensions
                {
                    public static Clients.IHttpClientBuilder AddStandardHedgingHandler(this Clients.IHttpClientBuilder builder) => builder;
                }
            }
            """;

        var compilation = TestCompilationFactory.Create("ReducedFromNamespace", source);
        TestCompilationFactory.EnsureNoCompilerErrors(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var invocation = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(node => node.ToString().Contains("AddStandardHedgingHandler", StringComparison.Ordinal));
        var method = Assert.IsAssignableFrom<IMethodSymbol>(model.GetSymbolInfo(invocation).Symbol);
        var original = method.ReducedFrom ?? method;
        var builderType = compilation.GetSymbolsWithName("IHttpClientBuilder").OfType<INamedTypeSymbol>().Single();
        var reduced = original.ReduceExtensionMethod(builderType);

        Assert.NotNull(reduced);
        Assert.True(ResilienceHandlerInvocation.IsFrameworkResilienceExtension(reduced));
        Assert.Equal(
            "Microsoft.Extensions.DependencyInjection",
            reduced.ContainingNamespace.ToDisplayString());
    }
}
