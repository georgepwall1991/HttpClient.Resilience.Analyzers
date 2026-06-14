using HttpClient.Resilience.Analyzers.Analyzers.ResponseLifetime;
using HttpClient.Resilience.Analyzers.CodeFixes;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.ResponseLifetime;

public sealed class HCR060_ResponseHeadersReadDisposalAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenResponseHeadersReadResultIsLocalVariable()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task UseAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    _ = await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR060, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenGetAsyncUsesResponseHeadersRead()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task UseAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    var response = await client.GetAsync("/events", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    _ = await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR060, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenCompletionOptionIsFullyQualified()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task UseAsync(
                    System.Net.Http.HttpClient client,
                    System.Net.Http.HttpRequestMessage request,
                    CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(
                        request,
                        System.Net.Http.HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);
                    _ = await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR060, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenCompletionOptionIsCustomLookalike()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public enum HttpCompletionOption
            {
                ResponseHeadersRead
            }

            public static class HttpClientExtensions
            {
                public static Task<HttpResponseMessage> SendAsync(
                    this HttpClient client,
                    string uri,
                    HttpCompletionOption completionOption,
                    CancellationToken cancellationToken)
                {
                    return Task.FromResult(new HttpResponseMessage());
                }
            }

            public sealed class Client
            {
                public async Task UseAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(
                        "/events",
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);
                    _ = await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResponseHeadersReadResultUsesUsingDeclaration()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task UseAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    _ = await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenUsingStatementOwnsResponse()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task UseAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                    {
                        _ = await response.Content.ReadAsStringAsync(cancellationToken);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenUsingStatementOwnsPreviouslyDeclaredResponse()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task UseAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    using (response)
                    {
                        _ = await response.Content.ReadAsStringAsync(cancellationToken);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenCompletionOptionIsNotResponseHeadersRead()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task UseAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, cancellationToken);
                    _ = await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenLocalOnlyStoresCompletionOption()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Client
            {
                public HttpCompletionOption Create()
                {
                    var option = HttpCompletionOption.ResponseHeadersRead;
                    return option;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenLookalikeMethodDoesNotReturnHttpResponseMessage()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;

            public sealed class Client
            {
                public void Use(NotHttpClient client, CancellationToken cancellationToken)
                {
                    var result = client.GetAsync("/events", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                }
            }

            public sealed class NotHttpClient
            {
                public string GetAsync(string path, HttpCompletionOption completionOption, CancellationToken cancellationToken) => path;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResolvedTypeIsCustomHttpClient()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task UseAsync(Custom.HttpClient client, CancellationToken cancellationToken)
                {
                    var result = await client.GetAsync("/events", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                }
            }

            namespace Custom
            {
                public sealed class HttpClient
                {
                    public Task<string> GetAsync(string path, HttpCompletionOption completionOption, CancellationToken cancellationToken)
                    {
                        return Task.FromResult(path);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenLocalStoresResponseTask()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<HttpResponseMessage> UseAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var responseTask = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    return await responseTask;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResponseIsReturnedToCaller()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<HttpResponseMessage> UseAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    return response;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenOnlyResponseContentStreamIsReturned()
    {
        const string source = """
            using System.IO;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<Stream> UseAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    return await response.Content.ReadAsStreamAsync(cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR060, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenReturnExpressionOnlyUsesResponseMember()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<HttpContent> UseAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    return response.Content;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR060, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenResponseIsTransferredToReturnedWrapper()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<ResponseOwner> UseAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    return new ResponseOwner(response);
                }
            }

            public sealed class ResponseOwner(HttpResponseMessage response)
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResponseIsTransferredToReturnedWrapperInitializer()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<ResponseOwner> UseAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    return new ResponseOwner { Response = response };
                }
            }

            public sealed class ResponseOwner
            {
                public HttpResponseMessage? Response { get; init; }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResponseIsTransferredToReturnedWrapperLocal()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<ResponseOwner> UseAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    var owner = new ResponseOwner(response);
                    return owner;
                }
            }

            public sealed class ResponseOwner(HttpResponseMessage response)
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResponseIsTransferredToReturnedWrapperInitializerLocal()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<ResponseOwner> UseAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    var owner = new ResponseOwner { Response = response };
                    return owner;
                }
            }

            public sealed class ResponseOwner
            {
                public HttpResponseMessage? Response { get; init; }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResponseIsExplicitlyDisposedInBlock()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task UseAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    _ = await response.Content.ReadAsStringAsync(cancellationToken);
                    response.Dispose();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResponseIsDisposedInFinally()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task UseAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    try
                    {
                        _ = await response.Content.ReadAsStringAsync(cancellationToken);
                    }
                    finally
                    {
                        response.Dispose();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenResponseIsOnlyConditionallyDisposed()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task UseAsync(
                    HttpClient client,
                    HttpRequestMessage request,
                    bool dispose,
                    CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    _ = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (dispose)
                    {
                        response.Dispose();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR060, diagnostic.Id);
    }

    [Fact]
    public async Task CodeFix_AddsUsingDeclaration()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task UseAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    _ = await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        const string expected = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task UseAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    _ = await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer, HCR060_DisposeResponseCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Equal(NormalizeLineEndings(expected), NormalizeLineEndings(fixedSource));
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n");
    }
}
