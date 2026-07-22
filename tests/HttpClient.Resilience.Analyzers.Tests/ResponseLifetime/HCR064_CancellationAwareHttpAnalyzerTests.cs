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
    public async Task DoesNotReport_WhenCustomExtensionOnHttpClientHasTokenOverload()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class CustomExtensions
            {
                public static Task<string> GetAsync(this HttpClient client, int key)
                {
                    return Task.FromResult(key.ToString());
                }

                public static Task<string> GetAsync(
                    this HttpClient client,
                    int key,
                    CancellationToken cancellationToken)
                {
                    return Task.FromResult(key.ToString());
                }
            }

            public sealed class Client
            {
                public Task<string> GetAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    return client.GetAsync(42);
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

    [Fact]
    public async Task ReportsDiagnostic_WhenJsonContentReadOmitsAvailableCancellationToken()
    {
        const string source = """
            using System.Net.Http;
            using System.Net.Http.Json;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<Order?> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
                {
                    return response.Content.ReadFromJsonAsync<Order>();
                }
            }

            public sealed class Order
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR064_CancellationAwareHttpAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR064, diagnostic.Id);
    }

    [Fact]
    public async Task CodeFix_PassesTokenToJsonContentRead()
    {
        const string source = """
            using System.Net.Http;
            using System.Net.Http.Json;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<Order?> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
                {
                    return response.Content.ReadFromJsonAsync<Order>();
                }
            }

            public sealed class Order
            {
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR064_CancellationAwareHttpAnalyzer, HCR064_PassCancellationTokenCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "response.Content.ReadFromJsonAsync<Order>(cancellationToken: cancellationToken)",
            fixedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_PassesTokenToJsonStreamingRead()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Net.Http;
            using System.Net.Http.Json;
            using System.Threading;

            public sealed class Client
            {
                public IAsyncEnumerable<Order?> Stream(
                    HttpResponseMessage response,
                    CancellationToken cancellationToken)
                {
                    return response.Content.ReadFromJsonAsAsyncEnumerable<Order>();
                }
            }

            public sealed class Order
            {
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR064_CancellationAwareHttpAnalyzer, HCR064_PassCancellationTokenCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "response.Content.ReadFromJsonAsAsyncEnumerable<Order>(cancellationToken: cancellationToken)",
            fixedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_PassesTokenToSynchronousSend()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;

            public sealed class Client
            {
                public HttpResponseMessage Send(
                    HttpClient client,
                    HttpRequestMessage request,
                    CancellationToken cancellationToken)
                {
                    return client.Send(request);
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR064_CancellationAwareHttpAnalyzer, HCR064_PassCancellationTokenCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "client.Send(request, cancellationToken: cancellationToken)",
            fixedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_PassesTokenToSynchronousStreamRead()
    {
        const string source = """
            using System.IO;
            using System.Net.Http;
            using System.Threading;

            public sealed class Client
            {
                public Stream Open(HttpResponseMessage response, CancellationToken cancellationToken)
                {
                    return response.Content.ReadAsStream();
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR064_CancellationAwareHttpAnalyzer, HCR064_PassCancellationTokenCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "response.Content.ReadAsStream(cancellationToken: cancellationToken)",
            fixedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenCancellationTokenNoneIgnoresAvailableToken()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<HttpResponseMessage> GetAsync(
                    HttpClient client,
                    CancellationToken cancellationToken)
                {
                    return client.GetAsync("https://example.com", CancellationToken.None);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR064_CancellationAwareHttpAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR064, diagnostic.Id);
    }

    [Fact]
    public async Task CodeFix_ReplacesCancellationTokenNoneWithAvailableToken()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<HttpResponseMessage> GetAsync(
                    HttpClient client,
                    CancellationToken cancellationToken)
                {
                    return client.GetAsync("https://example.com", CancellationToken.None);
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR064_CancellationAwareHttpAnalyzer, HCR064_PassCancellationTokenCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "client.GetAsync(\"https://example.com\", cancellationToken: cancellationToken)",
            fixedSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CancellationToken.None", fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenDefaultTokenIgnoresAvailableToken()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<HttpResponseMessage> GetAsync(
                    HttpClient client,
                    CancellationToken cancellationToken)
                {
                    return client.GetAsync("https://example.com", cancellationToken: default);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR064_CancellationAwareHttpAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR064, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTypedDefaultTokenIgnoresAvailableToken()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<HttpResponseMessage> GetAsync(
                    HttpClient client,
                    CancellationToken cancellationToken)
                {
                    return client.GetAsync("https://example.com", default(CancellationToken));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR064_CancellationAwareHttpAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR064, diagnostic.Id);
    }

    [Fact]
    public async Task CodeFix_ReplacesDefaultTokenWithAvailableToken()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<HttpResponseMessage> GetAsync(
                    HttpClient client,
                    CancellationToken cancellationToken)
                {
                    return client.GetAsync("https://example.com", cancellationToken: default);
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR064_CancellationAwareHttpAnalyzer, HCR064_PassCancellationTokenCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "cancellationToken: cancellationToken",
            fixedSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("default", fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_PreservesDefaultNonTokenArgument()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<HttpResponseMessage> PostAsync(
                    HttpClient client,
                    CancellationToken cancellationToken)
                {
                    return client.PostAsync("https://example.com", default);
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR064_CancellationAwareHttpAnalyzer, HCR064_PassCancellationTokenCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "client.PostAsync(\"https://example.com\", default, cancellationToken: cancellationToken)",
            fixedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_PassesTokenToGetFromJsonAsync()
    {
        const string source = """
            using System.Net.Http;
            using System.Net.Http.Json;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<Order?> GetAsync(
                    HttpClient client,
                    CancellationToken cancellationToken)
                {
                    return client.GetFromJsonAsync<Order>("https://example.com/orders");
                }
            }

            public sealed class Order
            {
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR064_CancellationAwareHttpAnalyzer, HCR064_PassCancellationTokenCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "client.GetFromJsonAsync<Order>(\"https://example.com/orders\", cancellationToken: cancellationToken)",
            fixedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_PassesTokenToDeleteFromJsonAsync()
    {
        const string source = """
            using System.Net.Http;
            using System.Net.Http.Json;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<Order?> DeleteAsync(
                    HttpClient client,
                    CancellationToken cancellationToken)
                {
                    return client.DeleteFromJsonAsync<Order>("https://example.com/orders/42");
                }
            }

            public sealed class Order
            {
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR064_CancellationAwareHttpAnalyzer, HCR064_PassCancellationTokenCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "client.DeleteFromJsonAsync<Order>(\"https://example.com/orders/42\", cancellationToken: cancellationToken)",
            fixedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_PassesTokenToGetFromJsonAsAsyncEnumerable()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Net.Http;
            using System.Net.Http.Json;
            using System.Threading;

            public sealed class Client
            {
                public IAsyncEnumerable<Order?> Stream(
                    HttpClient client,
                    CancellationToken cancellationToken)
                {
                    return client.GetFromJsonAsAsyncEnumerable<Order>("https://example.com/orders");
                }
            }

            public sealed class Order
            {
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR064_CancellationAwareHttpAnalyzer, HCR064_PassCancellationTokenCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "client.GetFromJsonAsAsyncEnumerable<Order>(\"https://example.com/orders\", cancellationToken: cancellationToken)",
            fixedSource,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PatchAsJsonAsync")]
    [InlineData("PostAsJsonAsync")]
    [InlineData("PutAsJsonAsync")]
    public async Task CodeFix_PassesTokenToJsonWrite(string methodName)
    {
        var source = $$"""
            using System.Net.Http;
            using System.Net.Http.Json;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<HttpResponseMessage> SendAsync(
                    HttpClient client,
                    Order order,
                    CancellationToken cancellationToken)
                {
                    return client.{{methodName}}("https://example.com/orders", order);
                }
            }

            public sealed class Order
            {
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR064_CancellationAwareHttpAnalyzer, HCR064_PassCancellationTokenCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            $"client.{methodName}(\"https://example.com/orders\", order, cancellationToken: cancellationToken)",
            fixedSource,
            StringComparison.Ordinal);
    }
}
