using System.Collections.Immutable;
using System.Text;
using HttpClient.Resilience.Analyzers.Analyzers.Resilience;
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
    private static string BuildHttpHeavySource(int clients, bool registerResilienceHandler)
    {
        var builder = new StringBuilder();
        builder.AppendLine("using System.Net.Http;");
        builder.AppendLine("using System.Threading;");
        builder.AppendLine("using System.Threading.Tasks;");
        builder.AppendLine();
        builder.AppendLine("public interface IServiceCollection { }");
        builder.AppendLine("public interface IHttpClientBuilder { }");
        builder.AppendLine();
        builder.AppendLine("public static class Registrations");
        builder.AppendLine("{");
        builder.AppendLine("    public static void Configure(IServiceCollection services)");
        builder.AppendLine("    {");
        for (var index = 0; index < clients; index++)
        {
            builder.Append("        services.AddHttpClient<Client").Append(index).Append(">()");
            builder.AppendLine(registerResilienceHandler ? ".AddStandardResilienceHandler();" : ";");
        }

        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services) => null!;");
        builder.AppendLine("    public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder) => builder;");
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
