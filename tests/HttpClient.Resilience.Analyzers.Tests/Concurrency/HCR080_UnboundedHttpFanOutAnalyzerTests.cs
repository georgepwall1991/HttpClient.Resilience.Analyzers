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
    public async Task ReportsDiagnostic_WhenFullyQualifiedTaskWhenAllFansOutHttpCalls()
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
                    return System.Threading.Tasks.Task.WhenAll(urls.Select(url => client.GetAsync(url, cancellationToken)));
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
    public async Task DoesNotReport_WhenLookalikeTaskWhenAllFansOutHttpCalls()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Net.Http;
            using System.Threading;

            public sealed class FanOutService
            {
                public object Send(HttpClient client, IEnumerable<string> urls, CancellationToken cancellationToken)
                {
                    return Task.WhenAll(urls.Select(url => client.GetAsync(url, cancellationToken)));
                }
            }

            public static class Task
            {
                public static object WhenAll<T>(IEnumerable<T> values) => new();
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenTaskWhenAllFansOutLookalikeGetAsyncCalls()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;

            public sealed class FanOutService
            {
                public Task SendAsync(CustomClient client, IEnumerable<string> urls)
                {
                    return Task.WhenAll(urls.Select(url => client.GetAsync(url)));
                }
            }

            public sealed class CustomClient
            {
                public Task<string> GetAsync(string value)
                {
                    return Task.FromResult(value);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenTaskWhenAllFansOutResolvedCustomHttpClient()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;

            public sealed class FanOutService
            {
                public Task SendAsync(Custom.HttpClient client, IEnumerable<string> urls)
                {
                    return Task.WhenAll(urls.Select(url => client.GetAsync(url)));
                }
            }

            namespace Custom
            {
                public sealed class HttpClient
                {
                    public Task<string> GetAsync(string value)
                    {
                        return Task.FromResult(value);
                    }
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
    public async Task DoesNotReport_WhenHttpClientFieldUsesInlineConnectionLimitedHandler()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class FanOutService
            {
                private readonly HttpClient _client = new(new SocketsHttpHandler
                {
                    MaxConnectionsPerServer = 8
                });

                public Task SendAsync(IEnumerable<string> urls, CancellationToken cancellationToken)
                {
                    return Task.WhenAll(urls.Select(url => _client.GetAsync(url, cancellationToken)));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenHttpClientFieldUsesConnectionLimitedHandlerField()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class FanOutService
            {
                private static readonly SocketsHttpHandler Handler = new()
                {
                    MaxConnectionsPerServer = 8
                };

                private readonly HttpClient _client = new(Handler);

                public Task SendAsync(IEnumerable<string> urls, CancellationToken cancellationToken)
                {
                    return Task.WhenAll(urls.Select(url => _client.GetAsync(url, cancellationToken)));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenHttpClientPropertyUsesInlineConnectionLimitedHandler()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class FanOutService
            {
                private HttpClient Client { get; } = new HttpClient(new SocketsHttpHandler
                {
                    MaxConnectionsPerServer = 8
                });

                public Task SendAsync(IEnumerable<string> urls, CancellationToken cancellationToken)
                {
                    return Task.WhenAll(urls.Select(url => Client.GetAsync(url, cancellationToken)));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenHttpClientFieldUsesUnboundedHandlerField()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class FanOutService
            {
                private static readonly SocketsHttpHandler Handler = new();

                private readonly HttpClient _client = new(Handler);

                public Task SendAsync(IEnumerable<string> urls, CancellationToken cancellationToken)
                {
                    return Task.WhenAll(urls.Select(url => _client.GetAsync(url, cancellationToken)));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR080, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenHttpClientFieldDoesNotLimitConnections()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class FanOutService
            {
                private readonly HttpClient _client = new();

                public Task SendAsync(IEnumerable<string> urls, CancellationToken cancellationToken)
                {
                    return Task.WhenAll(urls.Select(url => _client.GetAsync(url, cancellationToken)));
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
