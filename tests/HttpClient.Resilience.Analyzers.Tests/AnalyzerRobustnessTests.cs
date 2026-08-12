using System.Collections.Concurrent;
using System.Collections.Immutable;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Tests;

/// <summary>
/// A analyzer that throws is silently disabled by Roslyn behind an AD0001, so the rule
/// stops protecting the consumer without failing their build. These tests run every
/// shipped analyzer over deliberately hostile source and require that never to happen.
/// </summary>
public sealed class AnalyzerRobustnessTests
{
    [Theory]
    [MemberData(nameof(AnalyzerTypeNames))]
    public async Task AnalyzerNeverThrowsOnHostileSource(string analyzerTypeName)
    {
        var failures = new List<string>();

        foreach (var (name, source) in HostileSources)
        {
            var exceptions = await RunAsync(analyzerTypeName, source);
            failures.AddRange(exceptions.Select(exception => $"{name}: {exception}"));
        }

        Assert.True(
            failures.Count == 0,
            $"{analyzerTypeName} threw while analyzing hostile source:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    [Theory]
    [MemberData(nameof(AnalyzerTypeNames))]
    public async Task AnalyzerNeverThrowsOnTheCorpus(string analyzerTypeName)
    {
        var compilationWithAnalyzers = CreateCompilationWithAnalyzers(
            CorpusCompilationFactory.Create(),
            analyzerTypeName,
            out var exceptions);

        await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        Assert.True(
            exceptions.IsEmpty,
            $"{analyzerTypeName} threw while analyzing the corpus:{Environment.NewLine}{string.Join(Environment.NewLine, exceptions)}");
    }

    [Fact]
    public async Task AllAnalyzersTogetherNeverThrowOnHostileSource()
    {
        var failures = new List<string>();

        foreach (var (name, source) in HostileSources)
        {
            var compilation = TestCompilationFactory.Create("RobustnessTests", source);
            var exceptions = new ConcurrentBag<Exception>();
            var compilationWithAnalyzers = compilation.WithAnalyzers(
                AnalyzerCatalog.CreateAll(),
                CreateOptions(exceptions));

            await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
            failures.AddRange(exceptions.Select(exception => $"{name}: {exception}"));
        }

        Assert.True(
            failures.Count == 0,
            $"Analyzers threw while running together:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    [Fact]
    public void EveryShippedAnalyzerIsDiscovered()
    {
        var supported = AnalyzerCatalog.CreateAll()
            .SelectMany(analyzer => analyzer.SupportedDiagnostics)
            .Select(descriptor => descriptor.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            string.Join(", ", AnalyzerCatalog.DiagnosticIdsInOrder),
            string.Join(", ", supported));
    }

    public static TheoryData<string> AnalyzerTypeNames()
    {
        return AnalyzerCatalog.AnalyzerTypeNames();
    }

    private static async Task<ImmutableArray<Exception>> RunAsync(string analyzerTypeName, string source)
    {
        var compilation = TestCompilationFactory.Create("RobustnessTests", source);
        var compilationWithAnalyzers = CreateCompilationWithAnalyzers(compilation, analyzerTypeName, out var exceptions);

        await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        return exceptions.ToImmutableArray();
    }

    private static CompilationWithAnalyzers CreateCompilationWithAnalyzers(
        Compilation compilation,
        string analyzerTypeName,
        out ConcurrentBag<Exception> exceptions)
    {
        exceptions = new ConcurrentBag<Exception>();
        return compilation.WithAnalyzers(
            ImmutableArray.Create(AnalyzerCatalog.CreateByFullName(analyzerTypeName)),
            CreateOptions(exceptions));
    }

    private static CompilationWithAnalyzersOptions CreateOptions(ConcurrentBag<Exception> exceptions)
    {
        var captured = exceptions;
        return new CompilationWithAnalyzersOptions(
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            onAnalyzerException: (exception, _, _) => captured.Add(exception),
            concurrentAnalysis: true,
            logAnalyzerExecutionTime: false);
    }

    /// <summary>
    /// Shapes that historically break syntax-first analyzers: unresolved symbols, partial
    /// syntax, unusual declaration forms, and deep nesting.
    /// </summary>
    private static IEnumerable<(string Name, string Source)> HostileSources
    {
        get
        {
            yield return ("empty", string.Empty);

            yield return ("only-usings", "using System;\nusing System.Net.Http;\n");

            yield return ("unterminated-class", """
                using System.Net.Http;

                public sealed class Broken
                {
                    public HttpClient Create()
                """);

            yield return ("incomplete-member-access", """
                using System.Net.Http;

                public sealed class Broken
                {
                    public void Do(HttpClient client)
                    {
                        client.
                    }
                }
                """);

            yield return ("unresolved-types", """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddHttpClient<UnknownClient>().AddStandardResilienceHandler();
                        services.AddSingleton<UnknownJob>();
                    }
                }
                """);

            yield return ("unresolved-generic-arity", """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddHttpClient<A, B, C>();
                        services.AddSingleton<>();
                    }
                }
                """);

            yield return ("alias-qualified-names", """
                extern alias Missing;

                using Client = System.Net.Http.HttpClient;

                public sealed class Aliased
                {
                    private static readonly Client Shared = new Client();

                    public global::System.Net.Http.HttpClient Create()
                    {
                        return new global::System.Net.Http.HttpClient();
                    }
                }
                """);

            yield return ("file-scoped-namespace-and-records", """
                namespace Corpus.Hostile;

                using System.Net.Http;
                using System.Threading;
                using System.Threading.Tasks;

                public record struct RequestOptions(string Url);

                public record Envelope(HttpClient Client)
                {
                    public Task<HttpResponseMessage> SendAsync() => Client.GetAsync("/relative");
                }

                public sealed class Primary(HttpClient client)
                {
                    public Task<HttpResponseMessage> PostAsync(CancellationToken token) =>
                        client.PostAsync("/relative", null, token);
                }
                """);

            yield return ("nested-types", """
                using System.Net.Http;
                using System.Threading.Tasks;

                public sealed class Outer
                {
                    public sealed class Middle
                    {
                        public sealed class Inner
                        {
                            private readonly HttpClient _client = new();

                            public Task<HttpResponseMessage> PostAsync() => _client.PostAsync("/x", null);
                        }
                    }
                }
                """);

            yield return ("deeply-nested-expressions", """
                using System.Net.Http;
                using System.Threading.Tasks;

                public sealed class Deep
                {
                    public async Task RunAsync(HttpClient client)
                    {
                        if (true)
                        {
                            if (true)
                            {
                                if (true)
                                {
                                    if (true)
                                    {
                                        if (true)
                                        {
                                            using var response = await client.GetAsync("https://example.com");
                                            _ = await response.Content.ReadAsStringAsync();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                """);

            yield return ("lookalike-apis", """
                public sealed class HttpClient
                {
                    public string GetAsync(string url) => url;
                }

                public static class Registrations
                {
                    public static void Configure(object services)
                    {
                        _ = new HttpClient().GetAsync("https://example.com");
                    }

                    public static object AddHttpClient(this object services) => services;
                }
                """);

            yield return ("global-usings-and-top-level-statements", """
                global using System.Net.Http;

                var client = new HttpClient();
                var response = await client.GetAsync("https://example.com");
                System.Console.WriteLine(response.StatusCode);
                """);

            yield return ("empty-invocation-shapes", """
                public static class Shapes
                {
                    public static void Run()
                    {
                        Run();
                        ((System.Action)Run)();
                        _ = new[] { 1 }[0];
                    }
                }
                """);
        }
    }
}
