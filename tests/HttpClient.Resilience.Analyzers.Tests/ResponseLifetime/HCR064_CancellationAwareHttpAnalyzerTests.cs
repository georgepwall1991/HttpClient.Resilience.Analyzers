using HttpClient.Resilience.Analyzers.Analyzers.ResponseLifetime;
using HttpClient.Resilience.Analyzers.CodeFixes;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.ResponseLifetime;

public sealed class HCR064_CancellationAwareHttpAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenHttpClientAsyncCallOmitsAvailableCancellationToken()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<HttpResponseMessage> GetAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    return client.GetAsync("https://example.com");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR064_CancellationAwareHttpAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR064, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenHttpClientAsyncCallHasOtherOverloadArgumentButOmitsToken()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<HttpResponseMessage> GetAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    return client.GetAsync("https://example.com", HttpCompletionOption.ResponseHeadersRead);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR064_CancellationAwareHttpAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR064, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenHttpClientAsyncCallPassesCancellationToken()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<HttpResponseMessage> GetAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    return client.GetAsync("https://example.com", cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR064_CancellationAwareHttpAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenNoCancellationTokenIsVisible()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<HttpResponseMessage> GetAsync(HttpClient client)
                {
                    return client.GetAsync("https://example.com");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR064_CancellationAwareHttpAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenCancellationTokenLocalIsNotVisible()
    {
        const string source = """
            using System;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<HttpResponseMessage> GetAsync(HttpClient client)
                {
                    Action initialize = () =>
                    {
                        var cancellationToken = CancellationToken.None;
                    };

                    initialize();
                    return client.GetAsync("https://example.com");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR064_CancellationAwareHttpAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenLambdaCancellationTokenIsVisible()
    {
        const string source = """
            using System;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Func<CancellationToken, Task<HttpResponseMessage>> Create(HttpClient client)
                {
                    return ct => client.GetAsync("https://example.com");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR064_CancellationAwareHttpAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR064, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenPriorLocalCancellationTokenIsVisible()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<HttpResponseMessage> GetAsync(HttpClient client, CancellationToken sourceToken)
                {
                    var cancellationToken = sourceToken;
                    return client.GetAsync("https://example.com");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR064_CancellationAwareHttpAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR064, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenHttpContentReadOmitsAvailableCancellationToken()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<string> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
                {
                    return response.Content.ReadAsStringAsync();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR064_CancellationAwareHttpAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR064, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenResolvedCustomHttpClientAsyncCallOmitsToken()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<string> GetAsync(Custom.HttpClient client, CancellationToken cancellationToken)
                {
                    return client.GetAsync("https://example.com");
                }
            }

            namespace Custom
            {
                public sealed class HttpClient
                {
                    public Task<string> GetAsync(string url)
                    {
                        return Task.FromResult(url);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR064_CancellationAwareHttpAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task CodeFix_PassesVisibleMethodCancellationToken()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<HttpResponseMessage> GetAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    return client.GetAsync("https://example.com");
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR064_CancellationAwareHttpAnalyzer, HCR064_PassCancellationTokenCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "client.GetAsync(\"https://example.com\", cancellationToken: cancellationToken)",
            fixedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_PassesVisibleLambdaCancellationToken()
    {
        const string source = """
            using System;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Func<CancellationToken, Task<HttpResponseMessage>> Create(HttpClient client)
                {
                    return ct => client.GetAsync("https://example.com");
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR064_CancellationAwareHttpAnalyzer, HCR064_PassCancellationTokenCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "client.GetAsync(\"https://example.com\", cancellationToken: ct)",
            fixedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_OffersEachVisibleCancellationToken_WhenMultipleAreVisible()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<HttpResponseMessage> GetAsync(
                    HttpClient client,
                    CancellationToken callerToken,
                    CancellationToken timeoutToken)
                {
                    return client.GetAsync("https://example.com");
                }
            }
            """;

        var titles = await CodeFixVerifier<HCR064_CancellationAwareHttpAnalyzer, HCR064_PassCancellationTokenCodeFixProvider>
            .GetCodeFixTitlesAsync(source);

        Assert.Equal(2, titles.Count);
        Assert.Contains("Pass 'callerToken' cancellation token", titles);
        Assert.Contains("Pass 'timeoutToken' cancellation token", titles);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenCancellationTokenSourceIsVisible()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<HttpResponseMessage> GetAsync(HttpClient client)
                {
                    using var timeout = new CancellationTokenSource();
                    return client.GetAsync("https://example.com");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR064_CancellationAwareHttpAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR064, diagnostic.Id);
    }

    [Fact]
    public async Task CodeFix_PassesTokenFromVisibleCancellationTokenSource()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<HttpResponseMessage> GetAsync(HttpClient client)
                {
                    using var timeout = new CancellationTokenSource();
                    return client.GetAsync("https://example.com");
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR064_CancellationAwareHttpAnalyzer, HCR064_PassCancellationTokenCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "client.GetAsync(\"https://example.com\", cancellationToken: timeout.Token)",
            fixedSource,
            StringComparison.Ordinal);
    }
}
