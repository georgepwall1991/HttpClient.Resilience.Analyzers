using HttpClient.Resilience.Analyzers.Analyzers.ResponseLifetime;
using HttpClient.Resilience.Analyzers.CodeFixes;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.ResponseLifetime;

public sealed class HCR063_SyncOverAsyncHttpAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenHttpClientAsyncResultIsRead()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Client
            {
                public HttpResponseMessage Get(HttpClient client)
                {
                    return client.GetAsync("https://example.com").Result;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR063_SyncOverAsyncHttpAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR063, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenHttpClientAsyncWaitIsCalled()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Client
            {
                public void Send(HttpClient client)
                {
                    client.GetAsync("https://example.com").Wait();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR063_SyncOverAsyncHttpAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR063, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenHttpClientAsyncGetAwaiterGetResultIsCalled()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Client
            {
                public HttpResponseMessage Get(HttpClient client)
                {
                    return client.GetAsync("https://example.com").GetAwaiter().GetResult();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR063_SyncOverAsyncHttpAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR063, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenHttpClientAsyncLocalResultIsRead()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Client
            {
                public HttpResponseMessage Get(HttpClient client)
                {
                    var responseTask = client.GetAsync("https://example.com");
                    return responseTask.Result;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR063_SyncOverAsyncHttpAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR063, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenHttpContentAsyncResultIsRead()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Client
            {
                public string Get(HttpResponseMessage response)
                {
                    return response.Content.ReadAsStringAsync().Result;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR063_SyncOverAsyncHttpAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR063, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenHttpClientAsyncCallIsAwaited()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<HttpResponseMessage> GetAsync(HttpClient client)
                {
                    return await client.GetAsync("https://example.com");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR063_SyncOverAsyncHttpAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenTaskLocalIsReassignedBeforeResult()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public HttpResponseMessage Get(HttpClient client)
                {
                    var responseTask = client.GetAsync("https://example.com");
                    responseTask = Task.FromResult(new HttpResponseMessage());
                    return responseTask.Result;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR063_SyncOverAsyncHttpAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResolvedCustomHttpClientAsyncResultIsRead()
    {
        const string source = """
            using System.Threading.Tasks;

            public sealed class Client
            {
                public string Get(Custom.HttpClient client)
                {
                    return client.GetAsync("https://example.com").Result;
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

        var diagnostics = await AnalyzerVerifier<HCR063_SyncOverAsyncHttpAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task CodeFix_ReplacesResultWithAwaitInsideAsyncMethod()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<HttpResponseMessage> GetAsync(HttpClient client)
                {
                    return client.GetAsync("https://example.com").Result;
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR063_SyncOverAsyncHttpAnalyzer, HCR063_AwaitHttpOperationCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "return await client.GetAsync(\"https://example.com\");",
            fixedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_IsNotOfferedInsideSynchronousMethod()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Client
            {
                public HttpResponseMessage Get(HttpClient client)
                {
                    return client.GetAsync("https://example.com").Result;
                }
            }
            """;

        var titles = await CodeFixVerifier<HCR063_SyncOverAsyncHttpAnalyzer, HCR063_AwaitHttpOperationCodeFixProvider>
            .GetCodeFixTitlesAsync(source);

        Assert.Empty(titles);
    }

    [Fact]
    public async Task CodeFix_ParenthesizesAwaitWhenAccessingResultMember()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<int> GetStatusAsync(HttpClient client)
                {
                    return (int)client.GetAsync("https://example.com").Result.StatusCode;
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR063_SyncOverAsyncHttpAnalyzer, HCR063_AwaitHttpOperationCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "(await client.GetAsync(\"https://example.com\")).StatusCode",
            fixedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_ReplacesGetAwaiterGetResultInsideAsyncMethod()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<HttpResponseMessage> GetAsync(HttpClient client)
                {
                    return client.GetAsync("https://example.com").GetAwaiter().GetResult();
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR063_SyncOverAsyncHttpAnalyzer, HCR063_AwaitHttpOperationCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "return await client.GetAsync(\"https://example.com\");",
            fixedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_IsNotOfferedForGetAwaiterGetResultInsideSynchronousMethod()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Client
            {
                public HttpResponseMessage Get(HttpClient client)
                {
                    return client.GetAsync("https://example.com").GetAwaiter().GetResult();
                }
            }
            """;

        var titles = await CodeFixVerifier<HCR063_SyncOverAsyncHttpAnalyzer, HCR063_AwaitHttpOperationCodeFixProvider>
            .GetCodeFixTitlesAsync(source);

        Assert.Empty(titles);
    }

    [Fact]
    public async Task CodeFix_ReplacesParameterlessWaitInsideAsyncMethod()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client)
                {
                    client.GetAsync("https://example.com").Wait();
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR063_SyncOverAsyncHttpAnalyzer, HCR063_AwaitHttpOperationCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "await client.GetAsync(\"https://example.com\");",
            fixedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_IsNotOfferedForWaitWithTimeout()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client)
                {
                    client.GetAsync("https://example.com").Wait(1000);
                }
            }
            """;

        var titles = await CodeFixVerifier<HCR063_SyncOverAsyncHttpAnalyzer, HCR063_AwaitHttpOperationCodeFixProvider>
            .GetCodeFixTitlesAsync(source);

        Assert.Empty(titles);
    }

    [Fact]
    public async Task CodeFix_ReplacesJsonContentResultWithAwait()
    {
        const string source = """
            using System.Net.Http;
            using System.Net.Http.Json;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<Order?> ReadAsync(HttpResponseMessage response)
                {
                    return response.Content.ReadFromJsonAsync<Order>().Result;
                }
            }

            public sealed class Order
            {
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR063_SyncOverAsyncHttpAnalyzer, HCR063_AwaitHttpOperationCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "return await response.Content.ReadFromJsonAsync<Order>();",
            fixedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_ReplacesBlockingContentCopyWithAwait()
    {
        const string source = """
            using System.IO;
            using System.Net.Http;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task CopyAsync(HttpResponseMessage response, Stream destination)
                {
                    response.Content.CopyToAsync(destination).GetAwaiter().GetResult();
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR063_SyncOverAsyncHttpAnalyzer, HCR063_AwaitHttpOperationCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "await response.Content.CopyToAsync(destination);",
            fixedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_ReplacesBlockingContentBufferingWithAwait()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task BufferAsync(HttpResponseMessage response)
                {
                    response.Content.LoadIntoBufferAsync().Wait();
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR063_SyncOverAsyncHttpAnalyzer, HCR063_AwaitHttpOperationCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "await response.Content.LoadIntoBufferAsync();",
            fixedSource,
            StringComparison.Ordinal);
    }
}
