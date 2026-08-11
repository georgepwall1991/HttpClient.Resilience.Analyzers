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
    public async Task ReportsDiagnostic_WhenTaskWhenAllFansOutHttpCallsThroughQuerySyntax()
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
                    return Task.WhenAll(
                        from url in urls
                        select client.GetAsync(url, cancellationToken));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR080, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenQuerySyntaxUsesCustomSelectPattern()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class FanOutService
            {
                public Task SendAsync(HttpClient client, CustomSource urls, CancellationToken cancellationToken)
                {
                    return Task.WhenAll(
                        from url in urls
                        select client.GetAsync(url, cancellationToken));
                }
            }

            public sealed class CustomSource
            {
                public IEnumerable<TResult> Select<TResult>(Func<string, TResult> selector)
                {
                    return new[] { selector("/one") };
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("DeleteFromJsonAsync")]
    [InlineData("GetFromJsonAsync")]
    public async Task ReportsDiagnostic_WhenTaskWhenAllFansOutJsonReads(string methodName)
    {
        var source = $$"""
            using System.Collections.Generic;
            using System.Linq;
            using System.Net.Http;
            using System.Net.Http.Json;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class FanOutService
            {
                public Task SendAsync(
                    HttpClient client,
                    IEnumerable<string> urls,
                    CancellationToken cancellationToken)
                {
                    return Task.WhenAll(urls.Select(url =>
                        client.{{methodName}}<Order>(url, cancellationToken)));
                }
            }

            public sealed class Order
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR080, diagnostic.Id);
    }

    [Theory]
    [InlineData("PatchAsJsonAsync")]
    [InlineData("PostAsJsonAsync")]
    [InlineData("PutAsJsonAsync")]
    public async Task ReportsDiagnostic_WhenTaskWhenAllFansOutJsonWrites(string methodName)
    {
        var source = $$"""
            using System.Collections.Generic;
            using System.Linq;
            using System.Net.Http;
            using System.Net.Http.Json;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class FanOutService
            {
                public Task SendAsync(
                    HttpClient client,
                    IEnumerable<Order> orders,
                    CancellationToken cancellationToken)
                {
                    return Task.WhenAll(orders.Select(order =>
                        client.{{methodName}}("/orders", order, cancellationToken)));
                }
            }

            public sealed class Order
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR080, diagnostic.Id);
    }

    [Theory]
    [InlineData("GetByteArrayAsync")]
    [InlineData("GetStreamAsync")]
    [InlineData("GetStringAsync")]
    public async Task ReportsDiagnostic_WhenTaskWhenAllFansOutBodyHelpers(string methodName)
    {
        var source = $$"""
            using System.Collections.Generic;
            using System.Linq;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class FanOutService
            {
                public Task SendAsync(
                    HttpClient client,
                    IEnumerable<string> urls,
                    CancellationToken cancellationToken)
                {
                    return Task.WhenAll(urls.Select(url =>
                        client.{{methodName}}(url, cancellationToken)));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR080, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenTaskWhenAllFansOutCustomJsonExtension()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Net.Http;
            using System.Threading.Tasks;

            public static class CustomHttpClientExtensions
            {
                public static Task<T?> GetFromJsonAsync<T>(this HttpClient client, int key)
                {
                    return Task.FromResult(default(T));
                }
            }

            public sealed class FanOutService
            {
                public Task SendAsync(HttpClient client, IEnumerable<int> keys)
                {
                    return Task.WhenAll(keys.Select(key => client.GetFromJsonAsync<Order>(key)));
                }
            }

            public sealed class Order
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
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
    public async Task ReportsDiagnostic_WhenTaskWhenAllUsesLocalHttpSelectTasks()
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
                    var tasks = urls.Select(url => client.GetAsync(url, cancellationToken));
                    return Task.WhenAll(tasks);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR080, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTaskWhenAllUsesNullForgivingTaskSequence()
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
                    var tasks = urls.Select(url => client.GetAsync(url, cancellationToken));
                    return Task.WhenAll(tasks!);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR080, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTaskSequenceInitializerIsNullForgiving()
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
                    var tasks = urls.Select(url => client.GetAsync(url, cancellationToken))!;
                    return Task.WhenAll(tasks);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR080, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenLocalHttpSelectTasksUseStandaloneAssignment()
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
                    IEnumerable<Task<HttpResponseMessage>> tasks;
                    tasks = urls.Select(url => client.GetAsync(url, cancellationToken));
                    return Task.WhenAll(tasks);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR080, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenLatestStandaloneTaskAssignmentFansOutHttpCalls()
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
                    var tasks = urls.Select(url => Task.FromResult(new HttpResponseMessage()));
                    tasks = urls.Select(url => client.GetAsync(url, cancellationToken));
                    return Task.WhenAll(tasks);
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
    public async Task DoesNotReport_WhenLocalTasksAreReassignedBeforeWhenAll()
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
                    var tasks = urls.Select(url => client.GetAsync(url, cancellationToken));
                    tasks = urls.Select(url => Task.FromResult(new HttpResponseMessage()));
                    return Task.WhenAll(tasks);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenLatestTaskAssignmentIsNestedInControlFlow()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class FanOutService
            {
                public Task SendAsync(
                    HttpClient client,
                    IEnumerable<string> urls,
                    bool useHttp,
                    CancellationToken cancellationToken)
                {
                    var tasks = urls.Select(url => Task.FromResult(new HttpResponseMessage()));
                    if (useHttp)
                    {
                        tasks = urls.Select(url => client.GetAsync(url, cancellationToken));
                    }

                    return Task.WhenAll(tasks);
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
    public async Task DoesNotReport_WhenTaskWhenAllUsesLookalikeSelect()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class FanOutService
            {
                public Task SendAsync(HttpClient client, CustomSource urls, CancellationToken cancellationToken)
                {
                    return Task.WhenAll(urls.Select(url => client.GetAsync(url, cancellationToken)));
                }
            }

            public sealed class CustomSource
            {
            }

            public static class CustomSourceExtensions
            {
                public static IEnumerable<TResult> Select<TResult>(
                    this CustomSource source,
                    Func<string, TResult> selector)
                {
                    return new[] { selector("/one") };
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
    public async Task DoesNotReport_WhenTaskWhenAllFansOutCustomExtensionOnHttpClient()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class CustomHttpClientExtensions
            {
                public static Task<string> GetAsync(
                    this HttpClient client,
                    int key,
                    CancellationToken cancellationToken)
                {
                    return Task.FromResult(key.ToString());
                }
            }

            public sealed class FanOutService
            {
                public Task SendAsync(
                    HttpClient client,
                    IEnumerable<int> keys,
                    CancellationToken cancellationToken)
                {
                    return Task.WhenAll(keys.Select(key => client.GetAsync(key, cancellationToken)));
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
    public async Task DoesNotReport_WhenTaskWhenAllFanOutIsGatedByThisQualifiedSemaphoreField()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class FanOutService
            {
                private readonly SemaphoreSlim _semaphore = new(8);

                public Task SendAsync(HttpClient client, IEnumerable<string> urls, CancellationToken cancellationToken)
                {
                    return Task.WhenAll(urls.Select(async url =>
                    {
                        await this._semaphore.WaitAsync(cancellationToken);
                        try
                        {
                            await client.GetAsync(url, cancellationToken);
                        }
                        finally
                        {
                            this._semaphore.Release();
                        }
                    }));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenSemaphoreWaitAndReleaseUseQualifiedAndUnqualifiedSameField()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class FanOutService
            {
                private readonly SemaphoreSlim _semaphore = new(8);

                public Task SendAsync(HttpClient client, IEnumerable<string> urls, CancellationToken cancellationToken)
                {
                    return Task.WhenAll(urls.Select(async url =>
                    {
                        await this._semaphore.WaitAsync(cancellationToken);
                        try
                        {
                            await client.GetAsync(url, cancellationToken);
                        }
                        finally
                        {
                            _semaphore.Release();
                        }
                    }));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenSemaphoreWaitAndReleaseUseDifferentReceivers()
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
                    var waitSemaphore = new SemaphoreSlim(8);
                    var releaseSemaphore = new SemaphoreSlim(8);
                    return Task.WhenAll(urls.Select(async url =>
                    {
                        await waitSemaphore.WaitAsync(cancellationToken);
                        try
                        {
                            await client.GetAsync(url, cancellationToken);
                        }
                        finally
                        {
                            releaseSemaphore.Release();
                        }
                    }));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR080, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenGateIsLookalikeWaitAndReleaseType()
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
                    var gate = new CustomGate();
                    return Task.WhenAll(urls.Select(async url =>
                    {
                        await gate.WaitAsync(cancellationToken);
                        try
                        {
                            await client.GetAsync(url, cancellationToken);
                        }
                        finally
                        {
                            gate.Release();
                        }
                    }));
                }
            }

            public sealed class CustomGate
            {
                public Task WaitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

                public void Release()
                {
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR080, diagnostic.Id);
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
    public async Task DoesNotReport_WhenHttpClientUsesNullForgivingInlineConnectionLimitedHandler()
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
                    }!)!;

                    return Task.WhenAll(urls.Select(url => client.GetAsync(url, cancellationToken)));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenHttpClientHandlerLimitsConnections()
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
                    using var client = new HttpClient(new HttpClientHandler
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
    public async Task ReportsDiagnostic_WhenHttpClientUsesLookalikeConnectionLimitedHandler()
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
                    using var client = new HttpClient(new Custom.SocketsHttpHandler
                    {
                        MaxConnectionsPerServer = 8
                    });

                    return Task.WhenAll(urls.Select(url => client.GetAsync(url, cancellationToken)));
                }
            }

            namespace Custom
            {
                public sealed class SocketsHttpHandler : HttpMessageHandler
                {
                    public int MaxConnectionsPerServer { get; set; }

                    protected override Task<HttpResponseMessage> SendAsync(
                        HttpRequestMessage request,
                        CancellationToken cancellationToken)
                    {
                        return Task.FromResult(new HttpResponseMessage());
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR080, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenHttpClientUsesLookalikeHttpClientHandler()
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
                    using var client = new HttpClient(new Custom.HttpClientHandler
                    {
                        MaxConnectionsPerServer = 8
                    });

                    return Task.WhenAll(urls.Select(url => client.GetAsync(url, cancellationToken)));
                }
            }

            namespace Custom
            {
                public sealed class HttpClientHandler : HttpMessageHandler
                {
                    public int MaxConnectionsPerServer { get; set; }

                    protected override Task<HttpResponseMessage> SendAsync(
                        HttpRequestMessage request,
                        CancellationToken cancellationToken)
                    {
                        return Task.FromResult(new HttpResponseMessage());
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR080, diagnostic.Id);
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
    public async Task DoesNotReport_WhenHttpClientUsesNullForgivingConnectionLimitedHandlerVariable()
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
                    }!;
                    using var client = new HttpClient(handler!);

                    return Task.WhenAll(urls.Select(url => client.GetAsync(url, cancellationToken)));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenConnectionLimitedHandlerVariableIsReassignedBeforeClientCreation()
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
                    handler = new SocketsHttpHandler();
                    using var client = new HttpClient(handler);

                    return Task.WhenAll(urls.Select(url => client.GetAsync(url, cancellationToken)));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR080, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenConnectionLimitedHttpClientLocalIsReassignedBeforeFanOut()
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
                    var client = new HttpClient(new SocketsHttpHandler
                    {
                        MaxConnectionsPerServer = 8
                    });
                    client = new HttpClient();

                    return Task.WhenAll(urls.Select(url => client.GetAsync(url, cancellationToken)));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR080_UnboundedHttpFanOutAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR080, diagnostic.Id);
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
    public async Task DoesNotReport_WhenThisQualifiedHttpClientFieldUsesInlineConnectionLimitedHandler()
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
                    return Task.WhenAll(urls.Select(url => this._client.GetAsync(url, cancellationToken)));
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
    public async Task DoesNotReport_WhenHttpClientFieldUsesNullForgivingConnectionLimitedHandlerField()
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
                }!;

                private readonly HttpClient _client = new(Handler!)!;

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
    public async Task DoesNotReport_WhenThisQualifiedHttpClientPropertyUsesInlineConnectionLimitedHandler()
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
                    return Task.WhenAll(urls.Select(url => this.Client.GetAsync(url, cancellationToken)));
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
