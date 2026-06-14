using HttpClient.Resilience.Analyzers.Analyzers.Concurrency;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.Concurrency;

public sealed class HCR080_UnboundedHttpFanOutAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenTaskWhenAllFansOutHttpCalls()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class FanOutService
            {
                public Task SendAsync(HttpClient client, IEnumerable<string> urls, CancellationToken cancellationToken)
                {
                    return Task.WhenAll(urls.Select(url => client.GetAsync(url, cancellationToken)));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR080, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenTaskWhenAllDoesNotContainHttpCall()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;

            public sealed class FanOutService
            {
                public Task SendAsync(IEnumerable<string> urls)
                {
                    return Task.WhenAll(urls.Select(url => Task.FromResult(url)));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenTaskWhenAllFanOutIsGatedBySemaphore()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class FanOutService
            {
                public Task SendAsync(HttpClient client, IEnumerable<string> urls, CancellationToken cancellationToken)
                {
                    var semaphore = new SemaphoreSlim(8);
                    return Task.WhenAll(urls.Select(async url =>
                    {
                        await semaphore.WaitAsync(cancellationToken);
                        try
                        {
                            await client.GetAsync(url, cancellationToken);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenHttpClientUsesInlineConnectionLimitedHandler()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class FanOutService
            {
                public Task SendAsync(IEnumerable<string> urls, CancellationToken cancellationToken)
                {
                    using var client = new HttpClient(new SocketsHttpHandler
                    {
                        MaxConnectionsPerServer = 8
                    });

                    return Task.WhenAll(urls.Select(url => client.GetAsync(url, cancellationToken)));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenHttpClientUsesConnectionLimitedHandlerVariable()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class FanOutService
            {
                public Task SendAsync(IEnumerable<string> urls, CancellationToken cancellationToken)
                {
                    var handler = new SocketsHttpHandler
                    {
                        MaxConnectionsPerServer = 8
                    };
                    using var client = new HttpClient(handler);

                    return Task.WhenAll(urls.Select(url => client.GetAsync(url, cancellationToken)));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenHandlerDoesNotLimitConnections()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class FanOutService
            {
                public Task SendAsync(IEnumerable<string> urls, CancellationToken cancellationToken)
                {
                    using var client = new HttpClient(new SocketsHttpHandler());

                    return Task.WhenAll(urls.Select(url => client.GetAsync(url, cancellationToken)));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR080, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenParallelForEachAsyncUsesBoundedOptions()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class FanOutService
            {
                public Task SendAsync(HttpClient client, IEnumerable<string> urls, CancellationToken cancellationToken)
                {
                    return Parallel.ForEachAsync(urls, new ParallelOptions
                    {
                        MaxDegreeOfParallelism = 8,
                        CancellationToken = cancellationToken
                    }, async (url, ct) =>
                    {
                        await client.GetAsync(url, ct);
                    });
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }
}
