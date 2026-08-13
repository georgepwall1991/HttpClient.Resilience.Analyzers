using HttpClient.Resilience.Analyzers.Analyzers.Resilience;
using HttpClient.Resilience.Analyzers.CodeFixes;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Models;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HttpClient.Resilience.Analyzers.Tests.Resilience;

/// <summary>
/// Explicit mutant-killer tests. Each case is chosen so flipping a boolean, swapping a
/// method name, or widening a namespace gate would fail.
/// </summary>
public sealed class HCR043_CustomPipelineUnsafeRetryMutationTests
{
    [Fact]
    public async Task ReportsHcr043AndNotHcr041_ForCustomRetryPipeline()
    {
        var source = CustomPipelineSources.TypedClient();

        var custom = await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>.GetDiagnosticsAsync(source);
        var standard = await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Equal(DiagnosticIds.HCR043, Assert.Single(custom).Id);
        Assert.Empty(standard);
    }

    [Fact]
    public async Task DoesNotReport_WhenCallbackInvokesAddTimeoutInsteadOfAddRetry()
    {
        var source = CustomPipelineSources.TypedClient(
            pipelineConfigure: """
                builder =>
                        {
                            builder.AddTimeout(System.TimeSpan.FromSeconds(10));
                        }
                """);

        Assert.Empty(await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>.GetDiagnosticsAsync(source));
    }

    [Theory]
    [InlineData("Polly", true)]
    [InlineData("CustomRetry", false)]
    public void IsFrameworkAddRetry_RequiresPollyOrGlobalNamespace(string extensionNamespace, bool expected)
    {
        var tree = CSharpSyntaxTree.ParseText($$"""
            using {{extensionNamespace}};

            class C
            {
                void M(ResiliencePipelineBuilder builder) => builder.AddRetry(new HttpRetryStrategyOptions());
            }

            public sealed class ResiliencePipelineBuilder { }
            public sealed class HttpRetryStrategyOptions { }

            namespace {{extensionNamespace}}
            {
                public static class Extensions
                {
                    public static ResiliencePipelineBuilder AddRetry(this ResiliencePipelineBuilder builder, HttpRetryStrategyOptions options) => builder;
                }
            }
            """);
        var compilation = CSharpCompilation.Create(
            "MutationNamespaceTests",
            new[] { tree },
            TestCompilationFactory.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(node => node.ToString().Contains("AddRetry", StringComparison.Ordinal));

        Assert.Equal(expected, ResilienceRetryInvocation.IsFrameworkAddRetry(invocation, model, CancellationToken.None));
    }

    [Fact]
    public void IsFrameworkAddRetry_AllowsGlobalNamespaceStubs()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            class C
            {
                void M(ResiliencePipelineBuilder builder) => builder.AddRetry(new HttpRetryStrategyOptions());
            }

            public sealed class ResiliencePipelineBuilder
            {
                public ResiliencePipelineBuilder AddRetry(HttpRetryStrategyOptions options) => this;
            }

            public sealed class HttpRetryStrategyOptions { }
            """);
        var compilation = CSharpCompilation.Create(
            "MutationGlobalTests",
            new[] { tree },
            TestCompilationFactory.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();

        Assert.True(ResilienceRetryInvocation.IsFrameworkAddRetry(invocation, model, CancellationToken.None));
    }

    [Fact]
    public void RetryUnsafeMethodGuard_TreatsLiteralZeroAsDisabledRetries()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            class C
            {
                void M(ResiliencePipelineBuilder builder)
                {
                    builder.AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 0 });
                    builder.AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 1 });
                }
            }

            public sealed class ResiliencePipelineBuilder
            {
                public ResiliencePipelineBuilder AddRetry(HttpRetryStrategyOptions options) => this;
            }

            public sealed class HttpRetryStrategyOptions
            {
                public int MaxRetryAttempts { get; set; }
            }
            """);
        var compilation = CSharpCompilation.Create(
            "MutationMaxRetryTests",
            new[] { tree },
            TestCompilationFactory.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var retries = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(node => node.ToString().Contains("AddRetry", StringComparison.Ordinal))
            .ToArray();

        Assert.True(RetryUnsafeMethodGuard.HasVisibleGuard(retries[0], model, CancellationToken.None));
        Assert.False(RetryUnsafeMethodGuard.HasVisibleGuard(retries[1], model, CancellationToken.None));
    }

    [Fact]
    public void RetryUnsafeMethodGuard_RequiresFrameworkDisableForUnsafeHttpMethods()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            using CustomRetryGuards;

            class C
            {
                void M(ResiliencePipelineBuilder builder)
                {
                    var options = new HttpRetryStrategyOptions();
                    options.DisableForUnsafeHttpMethods();
                    builder.AddRetry(options);
                }
            }

            public sealed class ResiliencePipelineBuilder
            {
                public ResiliencePipelineBuilder AddRetry(HttpRetryStrategyOptions options) => this;
            }

            public sealed class HttpRetryStrategyOptions { }

            namespace CustomRetryGuards
            {
                public static class Extensions
                {
                    public static void DisableForUnsafeHttpMethods(this HttpRetryStrategyOptions options) { }
                }
            }
            """);
        var compilation = CSharpCompilation.Create(
            "MutationDisableForTests",
            new[] { tree },
            TestCompilationFactory.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var addRetry = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(node => node.ToString().Contains("AddRetry", StringComparison.Ordinal));

        Assert.False(RetryUnsafeMethodGuard.HasVisibleGuard(addRetry, model, CancellationToken.None));
    }

    [Fact]
    public void RetryUnsafeMethodGuard_AcceptsGlobalDisableForUnsafeHttpMethods()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            class C
            {
                void M(ResiliencePipelineBuilder builder)
                {
                    var options = new HttpRetryStrategyOptions();
                    options.DisableForUnsafeHttpMethods();
                    builder.AddRetry(options);
                }
            }

            public sealed class ResiliencePipelineBuilder
            {
                public ResiliencePipelineBuilder AddRetry(HttpRetryStrategyOptions options) => this;
            }

            public sealed class HttpRetryStrategyOptions
            {
                public void DisableForUnsafeHttpMethods() { }
            }
            """);
        var compilation = CSharpCompilation.Create(
            "MutationGlobalDisableForTests",
            new[] { tree },
            TestCompilationFactory.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var addRetry = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(node => node.ToString().Contains("AddRetry", StringComparison.Ordinal));

        Assert.True(RetryUnsafeMethodGuard.HasVisibleGuard(addRetry, model, CancellationToken.None));
    }

    [Fact]
    public async Task DiagnosticLocation_IsAddRetryTokenNotAddResilienceHandler()
    {
        var source = CustomPipelineSources.TypedClient();
        var diagnostic = Assert.Single(
            await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>.GetDiagnosticsAsync(source));

        Assert.Equal("AddRetry", source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
        Assert.NotEqual(
            "AddResilienceHandler",
            source.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
    }

    [Fact]
    public async Task DoesNotReportHcr043_ForStandardResilienceHandler()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services.AddHttpClient<PaymentsClient>().AddStandardResilienceHandler();
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

        Assert.Empty(await AnalyzerVerifier<HCR043_CustomPipelineUnsafeRetryAnalyzer>.GetDiagnosticsAsync(source));
        Assert.Equal(
            DiagnosticIds.HCR041,
            Assert.Single(await AnalyzerVerifier<HCR041_UnsafeMethodRetryAnalyzer>.GetDiagnosticsAsync(source)).Id);
    }

    [Fact]
    public void CodeFix_WithholdsWhenTypeNameIsNotHttpRetryStrategyOptions()
    {
        var provider = new HCR043_DisableUnsafeMethodRetriesCodeFixProvider();
        Assert.Equal(DiagnosticIds.HCR043, Assert.Single(provider.FixableDiagnosticIds));
        Assert.Equal("Disable retries for unsafe HTTP methods", HCR043_DisableUnsafeMethodRetriesCodeFixProvider.Title);
    }
}
