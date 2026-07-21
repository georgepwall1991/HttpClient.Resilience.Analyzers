using HttpClient.Resilience.Analyzers.Analyzers.ResponseLifetime;
using HttpClient.Resilience.Analyzers.CodeFixes;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.ResponseLifetime;

public sealed class HCR061_UnsuccessfulResponseIgnoredAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenContentIsReadBeforeSuccessIsChecked()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<string> GetAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    var response = await client.GetAsync("https://example.com", cancellationToken);
                    return await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR061_UnsuccessfulResponseIgnoredAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR061, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenUsingDeclarationDisposesButDoesNotCheckSuccess()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<string> GetAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    using var response = await client.GetAsync("https://example.com", cancellationToken);
                    return await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR061_UnsuccessfulResponseIgnoredAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR061, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenEnsureSuccessStatusCodeIsCalledBeforeContentRead()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<string> GetAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    var response = await client.GetAsync("https://example.com", cancellationToken);
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR061_UnsuccessfulResponseIgnoredAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenIsSuccessStatusCodeIsCheckedBeforeContentRead()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<string> GetAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    var response = await client.GetAsync("https://example.com", cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        return string.Empty;
                    }

                    return await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR061_UnsuccessfulResponseIgnoredAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenStatusCodeIsCheckedBeforeContentRead()
    {
        const string source = """
            using System.Net;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<string> GetAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    var response = await client.GetAsync("https://example.com", cancellationToken);
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        return string.Empty;
                    }

                    return await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR061_UnsuccessfulResponseIgnoredAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResponseIsReturnedWithoutContentRead()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<HttpResponseMessage> GetAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    var response = await client.GetAsync("https://example.com", cancellationToken);
                    return response;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR061_UnsuccessfulResponseIgnoredAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResponseLocalIsReassignedBeforeContentRead()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<string> GetAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    var response = await client.GetAsync("https://example.com", cancellationToken);
                    response = new HttpResponseMessage();
                    return await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR061_UnsuccessfulResponseIgnoredAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResolvedCustomHttpClientReturnsResponseLikeType()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<string> GetAsync(Custom.HttpClient client, CancellationToken cancellationToken)
                {
                    var response = await client.GetAsync("https://example.com", cancellationToken);
                    return await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }

            namespace Custom
            {
                public sealed class HttpClient
                {
                    public Task<Response> GetAsync(string url, CancellationToken cancellationToken)
                    {
                        return Task.FromResult(new Response());
                    }
                }

                public sealed class Response
                {
                    public Content Content { get; } = new();
                }

                public sealed class Content
                {
                    public Task<string> ReadAsStringAsync(CancellationToken cancellationToken)
                    {
                        return Task.FromResult(string.Empty);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR061_UnsuccessfulResponseIgnoredAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task CodeFix_InsertsEnsureSuccessStatusCodeBeforeContentRead()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<string> GetAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    var response = await client.GetAsync("https://example.com", cancellationToken);
                    return await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR061_UnsuccessfulResponseIgnoredAnalyzer, HCR061_EnsureSuccessStatusCodeCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        var successCheckIndex = fixedSource.IndexOf("response.EnsureSuccessStatusCode();", StringComparison.Ordinal);
        var contentReadIndex = fixedSource.IndexOf("response.Content.ReadAsStringAsync", StringComparison.Ordinal);

        Assert.True(successCheckIndex >= 0);
        Assert.True(contentReadIndex > successCheckIndex);
    }

    [Fact]
    public async Task CodeFix_InsertsSuccessCheckWhenContentReadIsNested()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<string> GetAsync(HttpClient client, bool readContent, CancellationToken cancellationToken)
                {
                    var response = await client.GetAsync("https://example.com", cancellationToken);
                    if (readContent)
                    {
                        return await response.Content.ReadAsStringAsync(cancellationToken);
                    }

                    return string.Empty;
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR061_UnsuccessfulResponseIgnoredAnalyzer, HCR061_EnsureSuccessStatusCodeCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        var declarationIndex = fixedSource.IndexOf("var response =", StringComparison.Ordinal);
        var successCheckIndex = fixedSource.IndexOf("response.EnsureSuccessStatusCode();", StringComparison.Ordinal);
        var branchIndex = fixedSource.IndexOf("if (readContent)", StringComparison.Ordinal);

        Assert.True(successCheckIndex > declarationIndex);
        Assert.True(branchIndex > successCheckIndex);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenAssignedResponseContentIsReadWithoutSuccessCheck()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<string> GetAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    HttpResponseMessage response;
                    response = await client.GetAsync("https://example.com", cancellationToken);
                    return await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR061_UnsuccessfulResponseIgnoredAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR061, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenAssignedResponseIsCheckedBeforeContentRead()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<string> GetAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    HttpResponseMessage response;
                    response = await client.GetAsync("https://example.com", cancellationToken);
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR061_UnsuccessfulResponseIgnoredAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task CodeFix_InsertsSuccessCheckAfterResponseAssignment()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<string> GetAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    HttpResponseMessage response;
                    response = await client.GetAsync("https://example.com", cancellationToken);
                    return await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR061_UnsuccessfulResponseIgnoredAnalyzer, HCR061_EnsureSuccessStatusCodeCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        var assignmentIndex = fixedSource.IndexOf("response = await", StringComparison.Ordinal);
        var successCheckIndex = fixedSource.IndexOf("response.EnsureSuccessStatusCode();", StringComparison.Ordinal);
        var contentReadIndex = fixedSource.IndexOf("response.Content.ReadAsStringAsync", StringComparison.Ordinal);

        Assert.True(successCheckIndex > assignmentIndex);
        Assert.True(contentReadIndex > successCheckIndex);
    }

    [Fact]
    public async Task CodeFix_InsertsSuccessCheckBeforeJsonContentRead()
    {
        const string source = """
            using System.Net.Http;
            using System.Net.Http.Json;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<Order?> GetAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    var response = await client.GetAsync("https://example.com/order", cancellationToken);
                    return await response.Content.ReadFromJsonAsync<Order>(cancellationToken);
                }
            }

            public sealed class Order
            {
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR061_UnsuccessfulResponseIgnoredAnalyzer, HCR061_EnsureSuccessStatusCodeCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        var successCheckIndex = fixedSource.IndexOf("response.EnsureSuccessStatusCode();", StringComparison.Ordinal);
        var jsonReadIndex = fixedSource.IndexOf("response.Content.ReadFromJsonAsync", StringComparison.Ordinal);

        Assert.True(successCheckIndex >= 0);
        Assert.True(jsonReadIndex > successCheckIndex);
    }

    [Fact]
    public async Task CodeFix_InsertsSuccessCheckAfterSynchronousSend()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public Task<string> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = client.Send(request, cancellationToken);
                    return response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR061_UnsuccessfulResponseIgnoredAnalyzer, HCR061_EnsureSuccessStatusCodeCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        var sendIndex = fixedSource.IndexOf("response = client.Send", StringComparison.Ordinal);
        var successCheckIndex = fixedSource.IndexOf("response.EnsureSuccessStatusCode();", StringComparison.Ordinal);
        var contentReadIndex = fixedSource.IndexOf("response.Content.ReadAsStringAsync", StringComparison.Ordinal);

        Assert.True(successCheckIndex > sendIndex);
        Assert.True(contentReadIndex > successCheckIndex);
    }

    [Fact]
    public async Task CodeFix_InsertsSuccessCheckBeforeSynchronousStreamRead()
    {
        const string source = """
            using System.IO;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<Stream> OpenAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    var response = await client.GetAsync("https://example.com", cancellationToken);
                    return response.Content.ReadAsStream(cancellationToken);
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR061_UnsuccessfulResponseIgnoredAnalyzer, HCR061_EnsureSuccessStatusCodeCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        var successCheckIndex = fixedSource.IndexOf("response.EnsureSuccessStatusCode();", StringComparison.Ordinal);
        var streamReadIndex = fixedSource.IndexOf("response.Content.ReadAsStream", StringComparison.Ordinal);

        Assert.True(successCheckIndex >= 0);
        Assert.True(streamReadIndex > successCheckIndex);
    }

    [Fact]
    public async Task CodeFix_InsertsSuccessCheckBeforeJsonStreamingRead()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Net.Http;
            using System.Net.Http.Json;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<IAsyncEnumerable<Order?>> StreamAsync(
                    HttpClient client,
                    CancellationToken cancellationToken)
                {
                    var response = await client.GetAsync("https://example.com", cancellationToken);
                    return response.Content.ReadFromJsonAsAsyncEnumerable<Order>(cancellationToken: cancellationToken);
                }
            }

            public sealed class Order
            {
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR061_UnsuccessfulResponseIgnoredAnalyzer, HCR061_EnsureSuccessStatusCodeCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        var successCheckIndex = fixedSource.IndexOf("response.EnsureSuccessStatusCode();", StringComparison.Ordinal);
        var jsonReadIndex = fixedSource.IndexOf("response.Content.ReadFromJsonAsAsyncEnumerable", StringComparison.Ordinal);

        Assert.True(successCheckIndex >= 0);
        Assert.True(jsonReadIndex > successCheckIndex);
    }
}
