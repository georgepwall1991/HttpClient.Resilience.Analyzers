using HttpClient.Resilience.Analyzers.Analyzers.Lifetime;
using HttpClient.Resilience.Analyzers.CodeFixes;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.Lifetime;

public sealed class HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenStaticHttpClientHasDefaultHandler()
    {
        const string source = """
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static readonly HttpClient Client = new();
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR002, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenStaticHttpClientHasPooledConnectionLifetime()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static readonly HttpClient Client = new(
                    new SocketsHttpHandler
                    {
                        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                    });
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenHttpClientFieldIsNotStatic()
    {
        const string source = """
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private readonly HttpClient _client = new();
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task CodeFix_AddsSocketsHttpHandlerWithPooledConnectionLifetime()
    {
        const string source = """
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static readonly HttpClient Client = new();
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer, HCR002_AddPooledConnectionLifetimeCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains("new HttpClient(new SocketsHttpHandler", fixedSource);
        Assert.Contains("PooledConnectionLifetime = System.TimeSpan.FromMinutes(2)", fixedSource);
    }
}
