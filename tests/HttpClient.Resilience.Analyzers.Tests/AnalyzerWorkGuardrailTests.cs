using System.Collections.Immutable;
using System.Text;
using HttpClient.Resilience.Analyzers.Analyzers.Resilience;
using HttpClient.Resilience.Analyzers.Models;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Tests;

/// <summary>
/// Deterministic bounds on how much work the analyzers do. Counting work instead of timing
/// it keeps these assertions stable on shared CI machines while still failing loudly if a
/// whole-compilation scan is reintroduced on a hot path.
/// </summary>
public sealed class AnalyzerWorkGuardrailTests
{
    [Fact]
    public async Task Hcr041SkipsTheUnsafeCallIndexWhenNoResilienceHandlerIsRegistered()
    {
        var analyzer = new HCR041_UnsafeMethodRetryAnalyzer();

        await RunAsync(analyzer, BuildHttpHeavySource(clients: 40, registerResilienceHandler: false));

        Assert.Equal(0, analyzer.UnsafeCallIndexBuilds);
    }

    [Fact]
    public async Task Hcr041BuildsTheUnsafeCallIndexAtMostOncePerCompilation()
    {
        var analyzer = new HCR041_UnsafeMethodRetryAnalyzer();

        var diagnostics = await RunAsync(
            analyzer,
            BuildHttpHeavySource(clients: 40, registerResilienceHandler: true));

        Assert.Equal(1, analyzer.UnsafeCallIndexBuilds);
        Assert.Equal(40, diagnostics.Count(diagnostic => diagnostic.Id == "HCR041"));
    }

    [Fact]
    public async Task Hcr042SkipsTheUnsafeCallIndexWhenNoHedgingHandlerIsRegistered()
    {
        var analyzer = new HCR042_UnsafeMethodHedgingAnalyzer();

        await RunAsync(analyzer, BuildHttpHeavySource(clients: 40, registerResilienceHandler: false, registerHedgingHandler: false));

        Assert.Equal(0, analyzer.UnsafeCallIndexBuilds);
    }

    [Fact]
    public async Task Hcr042BuildsTheUnsafeCallIndexAtMostOncePerCompilation()
    {
        var analyzer = new HCR042_UnsafeMethodHedgingAnalyzer();

        var diagnostics = await RunAsync(
            analyzer,
            BuildHttpHeavySource(clients: 40, registerResilienceHandler: false, registerHedgingHandler: true));

        Assert.Equal(1, analyzer.UnsafeCallIndexBuilds);
        Assert.Equal(40, diagnostics.Count(diagnostic => diagnostic.Id == "HCR042"));
    }

    [Fact]
    public async Task Hcr042LeavesTheUnsafeCallIndexUnbuiltForLookalikeApis()
    {
        var analyzer = new HCR042_UnsafeMethodHedgingAnalyzer();

        await RunAsync(analyzer, """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Sender(HttpClient client)
            {
                public Task<HttpResponseMessage> PostAsync(CancellationToken token) =>
                    client.PostAsync("/x", null, token);
            }

            public static class NotDependencyInjection
            {
                public static object AddStandardHedgingHandler(this object builder) => builder;

                public static void Configure() => new object().AddStandardHedgingHandler();
            }
            """);

        Assert.Equal(0, analyzer.UnsafeCallIndexBuilds);
    }

    [Fact]
    public async Task Hcr043SkipsTheUnsafeCallIndexWhenNoCustomRetryPipelineIsRegistered()
    {
        var analyzer = new HCR043_CustomPipelineUnsafeRetryAnalyzer();

        await RunAsync(analyzer, BuildHttpHeavySource(clients: 40, registerResilienceHandler: false));

        Assert.Equal(0, analyzer.UnsafeCallIndexBuilds);
    }

    [Fact]
    public async Task Hcr043BuildsTheUnsafeCallIndexAtMostOncePerCompilation()
    {
        var analyzer = new HCR043_CustomPipelineUnsafeRetryAnalyzer();

        var diagnostics = await RunAsync(
            analyzer,
            BuildHttpHeavySource(clients: 40, registerResilienceHandler: false, registerCustomRetry: true));

        Assert.Equal(1, analyzer.UnsafeCallIndexBuilds);
        Assert.Equal(40, diagnostics.Count(diagnostic => diagnostic.Id == "HCR043"));
    }

    [Fact]
    public async Task Hcr043LeavesTheUnsafeCallIndexUnbuiltForLookalikeApis()
    {
        var analyzer = new HCR043_CustomPipelineUnsafeRetryAnalyzer();

        await RunAsync(analyzer, """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Sender(HttpClient client)
            {
                public Task<HttpResponseMessage> PostAsync(CancellationToken token) =>
                    client.PostAsync("/x", null, token);
            }

            public static class NotDependencyInjection
            {
                public static object AddResilienceHandler(this object builder, string name, System.Action<object> configure) => builder;

                public static void Configure() => new object().AddResilienceHandler("x", _ => { });
            }
            """);

        Assert.Equal(0, analyzer.UnsafeCallIndexBuilds);
    }

    [Fact]
    public async Task Hcr041Hcr042AndHcr043ShareOneUnsafeCallIndexBuild()
    {
        var retry = new HCR041_UnsafeMethodRetryAnalyzer();
        var hedging = new HCR042_UnsafeMethodHedgingAnalyzer();
        var custom = new HCR043_CustomPipelineUnsafeRetryAnalyzer();
        var source = BuildHttpHeavySource(
            clients: 8,
            registerResilienceHandler: true,
            registerHedgingHandler: true,
            registerCustomRetry: true);
        var compilation = TestCompilationFactory.Create("SharedUnsafeCallIndex", source);
        TestCompilationFactory.EnsureNoCompilerErrors(compilation);

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(retry, hedging, custom))
            .GetAnalyzerDiagnosticsAsync();

        Assert.Equal(8, diagnostics.Count(diagnostic => diagnostic.Id == "HCR041"));
        Assert.Equal(8, diagnostics.Count(diagnostic => diagnostic.Id == "HCR042"));
        Assert.Equal(8, diagnostics.Count(diagnostic => diagnostic.Id == "HCR043"));
        var builds = retry.UnsafeCallIndexBuilds + hedging.UnsafeCallIndexBuilds + custom.UnsafeCallIndexBuilds;
        Assert.Equal(1, builds);
    }

    [Fact]
    public async Task Hcr041LeavesTheUnsafeCallIndexUnbuiltForLookalikeApis()
    {
        var analyzer = new HCR041_UnsafeMethodRetryAnalyzer();

        await RunAsync(analyzer, """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Sender(HttpClient client)
            {
                public Task<HttpResponseMessage> PostAsync(CancellationToken token) =>
                    client.PostAsync("/x", null, token);
            }

            public static class NotDependencyInjection
            {
                public static object AddStandardResilienceHandler(this object builder) => builder;

                public static void Configure() => new object().AddStandardResilienceHandler();
            }
            """);

        Assert.Equal(0, analyzer.UnsafeCallIndexBuilds);
    }

    [Fact]
    public void RegistrationScanRunsOnceHoweverManyAnalyzersAskForIt()
    {
        var compilation = CorpusCompilationFactory.Create("SharedScanCompilation");

        for (var request = 0; request < 5; request++)
        {
            ServiceRegistrationCollector.CollectFrameworkRegistrations(compilation, CancellationToken.None);
        }

        Assert.Equal(1, ServiceRegistrationCollector.GetScanCount(compilation));
        Assert.Equal(1, CompilationSyntaxIndex.GetRootMaterializationCount(compilation));
    }

    [Fact]
    public void RegistrationScanIsNotSharedAcrossCompilations()
    {
        var scanned = CorpusCompilationFactory.Create("ScannedCompilation");
        var untouched = CorpusCompilationFactory.Create("UntouchedCompilation");

        ServiceRegistrationCollector.CollectFrameworkRegistrations(scanned, CancellationToken.None);

        Assert.Equal(1, ServiceRegistrationCollector.GetScanCount(scanned));
        Assert.Equal(0, ServiceRegistrationCollector.GetScanCount(untouched));
    }

    [Fact]
    public void RegistrationScanClassifiesReceiversOnlyForRegistrationMethodNames()
    {
        var compilation = TestCompilationFactory.Create("ReceiverClassificationTests", """
            public interface IServiceCollection { }

            public sealed class Noise
            {
                public Noise Chain() => this;

                public void Run()
                {
                    Chain().Chain().Chain().Chain().Chain();
                    Chain().Chain().Chain().Chain().Chain();
                }
            }

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<Noise>();
                    services.AddTransient<Noise>();
                }

                public static IServiceCollection AddSingleton<T>(this IServiceCollection services) => services;
                public static IServiceCollection AddTransient<T>(this IServiceCollection services) => services;
            }
            """);
        TestCompilationFactory.EnsureNoCompilerErrors(compilation);

        var registrations = ServiceRegistrationCollector.CollectFrameworkRegistrations(
            compilation,
            CancellationToken.None);

        Assert.Equal(2, registrations.Count);

        // Ten member invocations exist, eight of which are noise. Only the two whose method
        // name matches a registration API may reach the semantic receiver check.
        Assert.Equal(2, ServiceRegistrationCollector.GetReceiverClassificationCount(compilation));
    }

    [Fact]
    public void RegistrationScanWorkGrowsLinearlyWithCompilationSize()
    {
        var single = CorpusCompilationFactory.CreateScaled(1, "SingleCopyCompilation");
        var quadruple = CorpusCompilationFactory.CreateScaled(4, "QuadrupleCopyCompilation");

        ServiceRegistrationCollector.CollectFrameworkRegistrations(single, CancellationToken.None);
        ServiceRegistrationCollector.CollectFrameworkRegistrations(quadruple, CancellationToken.None);

        var singleCost = ServiceRegistrationCollector.GetReceiverClassificationCount(single);
        var quadrupleCost = ServiceRegistrationCollector.GetReceiverClassificationCount(quadruple);

        Assert.True(singleCost > 0, "The corpus must contain registration candidates for this bound to mean anything.");

        // Four copies of the same sources must cost four times as much, not more. A
        // superlinear result means a per-node rescan crept back in.
        Assert.Equal(singleCost * 4, quadrupleCost);
    }

    private static async Task<ImmutableArray<Diagnostic>> RunAsync(DiagnosticAnalyzer analyzer, string source)
    {
        var compilation = TestCompilationFactory.Create("WorkGuardrailTests", source);
        TestCompilationFactory.EnsureNoCompilerErrors(compilation);

        return await compilation
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync();
    }

    /// <summary>
    /// Generates a compilation with many HTTP-calling typed clients, so a rebuilt index or a
    /// per-node rescan would be obvious.
    /// </summary>
    private static string BuildHttpHeavySource(
        int clients,
        bool registerResilienceHandler,
        bool registerHedgingHandler = false,
        bool registerCustomRetry = false)
    {
        var builder = new StringBuilder();
        builder.AppendLine("using System.Net.Http;");
        builder.AppendLine("using System.Threading;");
        builder.AppendLine("using System.Threading.Tasks;");
        builder.AppendLine("using Polly;");
        builder.AppendLine();
        builder.AppendLine("public interface IServiceCollection { }");
        builder.AppendLine("public interface IHttpClientBuilder { }");
        builder.AppendLine("public sealed class ResiliencePipelineBuilder { }");
        builder.AppendLine("public sealed class HttpRetryStrategyOptions { }");
        builder.AppendLine();
        builder.AppendLine("public static class Registrations");
        builder.AppendLine("{");
        builder.AppendLine("    public static void Configure(IServiceCollection services)");
        builder.AppendLine("    {");
        for (var index = 0; index < clients; index++)
        {
            builder.Append("        services.AddHttpClient<Client").Append(index).Append(">()");
            if (registerResilienceHandler)
            {
                builder.Append(".AddStandardResilienceHandler()");
            }

            if (registerHedgingHandler)
            {
                builder.Append(".AddStandardHedgingHandler()");
            }

            if (registerCustomRetry)
            {
                builder.Append(".AddResilienceHandler(\"p\", pipeline => pipeline.AddRetry(new HttpRetryStrategyOptions()))");
            }

            builder.AppendLine(";");
        }

        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services) => null!;");
        builder.AppendLine("    public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder) => builder;");
        builder.AppendLine("    public static IHttpClientBuilder AddStandardHedgingHandler(this IHttpClientBuilder builder) => builder;");
        builder.AppendLine("    public static IHttpClientBuilder AddResilienceHandler(this IHttpClientBuilder builder, string name, System.Action<ResiliencePipelineBuilder> configure) => builder;");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("namespace Polly");
        builder.AppendLine("{");
        builder.AppendLine("    public static class RetryResiliencePipelineBuilderExtensions");
        builder.AppendLine("    {");
        builder.AppendLine("        public static ResiliencePipelineBuilder AddRetry(this ResiliencePipelineBuilder builder, HttpRetryStrategyOptions options) => builder;");
        builder.AppendLine("    }");
        builder.AppendLine("}");

        for (var index = 0; index < clients; index++)
        {
            builder.AppendLine();
            builder.Append("public sealed class Client").Append(index).AppendLine("(HttpClient httpClient)");
            builder.AppendLine("{");
            builder.AppendLine("    public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)");
            builder.AppendLine("    {");
            builder.Append("        var request = new HttpRequestMessage(HttpMethod.Get, \"/read").Append(index).AppendLine("\");");
            builder.AppendLine("        _ = request;");
            builder.Append("        return httpClient.PostAsync(\"/write").Append(index).AppendLine("\", null, cancellationToken);");
            builder.AppendLine("    }");
            builder.AppendLine("}");
        }

        return builder.ToString();
    }
}
