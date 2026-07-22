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
    public async Task ReportsDiagnostic_WhenExistingLocalIsAssignedResponseHeadersReadResult()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task UseAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    HttpResponseMessage response;
                    response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
    public async Task DoesNotReport_WhenUsingStatementOwnsResponseAlias()
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
                    var responseAlias = response;
                    using (responseAlias)
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
    public async Task DoesNotReport_WhenUsingStatementOwnsParenthesizedNullForgivingAlias()
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
                    var responseAlias = (response!);
                    using ((responseAlias!))
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
    public async Task DoesNotReport_WhenUsingStatementOwnsChainedAssignedResponseAlias()
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
                    var firstAlias = response;
                    HttpResponseMessage secondAlias;
                    secondAlias = firstAlias;
                    using (secondAlias)
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
    public async Task ReportsDiagnostic_WhenUsingStatementAliasIsReassignedBeforeOwnershipTransfer()
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
                    var responseAlias = response;
                    responseAlias = new HttpResponseMessage();
                    using (responseAlias)
                    {
                        _ = await response.Content.ReadAsStringAsync(cancellationToken);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR060, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenAssignedResponseIsDisposedInFinally()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task UseAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    HttpResponseMessage response;
                    response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
    public async Task DoesNotReport_WhenHttpClientExtensionMethodDoesNotReturnHttpResponseMessage()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class HttpClientExtensions
            {
                public static Task<string> GetAsync(
                    this HttpClient client,
                    int key,
                    HttpCompletionOption completionOption,
                    CancellationToken cancellationToken)
                {
                    return Task.FromResult(key.ToString());
                }
            }

            public sealed class Client
            {
                public async Task UseAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    var result = await client.GetAsync(
                        42,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenCustomExtensionReturnsHttpResponseMessage()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;

            public static class CustomExtensions
            {
                public static Task<HttpResponseMessage> SendAsync(
                    this global::System.Net.Http.HttpClient client,
                    int key,
                    HttpCompletionOption completionOption)
                {
                    return Task.FromResult(new HttpResponseMessage());
                }
            }

            public sealed class Client
            {
                public async Task UseAsync(global::System.Net.Http.HttpClient client)
                {
                    var response = await client.SendAsync(
                        42,
                        HttpCompletionOption.ResponseHeadersRead);
                }
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
    public async Task DoesNotReport_WhenOriginalResponseIsTransferredBeforeLocalReassignment()
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
                    response = new HttpResponseMessage();
                    response.Dispose();
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
    public async Task DoesNotReport_WhenResponseIsTransferredToUsingDeclaration()
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
                    using var owned = response;
                    _ = await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenParenthesizedNullForgivingResponseIsTransferredToUsingDeclaration()
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
                    using var owned = (response!);
                    _ = await owned.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenResponseLocalIsReassignedBeforeDispose()
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
                    response = new HttpResponseMessage();
                    response.Dispose();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR060, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenResponseLocalIsReassignedBeforeUsingDeclarationTransfer()
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
                    response = new HttpResponseMessage();
                    using var owned = response;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR060, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenResponseLocalIsReassignedBeforeReturn()
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
                    response = new HttpResponseMessage();
                    return response;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR060, diagnostic.Id);
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
    public async Task DoesNotReport_WhenParenthesizedNullForgivingResponseIsExplicitlyDisposed()
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
                    (response!).Dispose();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResponseIsDisposedThroughLocalAlias()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    var responseAlias = response;
                    responseAlias.Dispose();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenResponseIsReassignedBeforeDisposalAliasCapture()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response = new HttpResponseMessage();
                    var responseAlias = response;
                    responseAlias.Dispose();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR060, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenDisposalAliasIsReassignedBeforeDispose()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    var responseAlias = response;
                    responseAlias = new HttpResponseMessage();
                    responseAlias.Dispose();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR060, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenResponseIsDisposedThroughAssignedLocalAlias()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    HttpResponseMessage responseAlias;
                    responseAlias = response;
                    responseAlias.Dispose();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenResponseIsReassignedBeforeAssignedAliasCapture()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    HttpResponseMessage responseAlias;
                    response = new HttpResponseMessage();
                    responseAlias = response;
                    responseAlias.Dispose();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR060, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenAssignedDisposalAliasIsReassignedBeforeDispose()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    HttpResponseMessage responseAlias;
                    responseAlias = response;
                    responseAlias = new HttpResponseMessage();
                    responseAlias.Dispose();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR060, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenResponseIsDisposedThroughChainedLocalAliases()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    var firstAlias = response;
                    var secondAlias = firstAlias;
                    secondAlias.Dispose();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResponseIsDisposedThroughChainedAssignedAliases()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    HttpResponseMessage firstAlias;
                    HttpResponseMessage secondAlias;
                    firstAlias = response;
                    secondAlias = firstAlias;
                    secondAlias.Dispose();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenIntermediateDisposalAliasIsReassignedBeforeChaining()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    var firstAlias = response;
                    firstAlias = new HttpResponseMessage();
                    var secondAlias = firstAlias;
                    secondAlias.Dispose();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR060, diagnostic.Id);
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
    public async Task DoesNotReport_WhenResponseAliasIsDisposedInFinally()
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
                    var responseAlias = response;
                    try
                    {
                        _ = await response.Content.ReadAsStringAsync(cancellationToken);
                    }
                    finally
                    {
                        responseAlias.Dispose();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenChainedAssignedResponseAliasIsDisposedInFinally()
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
                    var firstAlias = response;
                    HttpResponseMessage secondAlias;
                    secondAlias = firstAlias;
                    try
                    {
                        _ = await response.Content.ReadAsStringAsync(cancellationToken);
                    }
                    finally
                    {
                        secondAlias.Dispose();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenResponseAliasIsReassignedBeforeFinallyDisposal()
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
                    var responseAlias = response;
                    responseAlias = new HttpResponseMessage();
                    try
                    {
                        _ = await response.Content.ReadAsStringAsync(cancellationToken);
                    }
                    finally
                    {
                        responseAlias.Dispose();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR060, diagnostic.Id);
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

    [Fact]
    public async Task CodeFix_MergesAdjacentDeclarationAndAssignment()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task UseAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    HttpResponseMessage response;
                    response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    _ = await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer, HCR060_DisposeResponseCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "using HttpResponseMessage response = await client.SendAsync",
            fixedSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("HttpResponseMessage response;", fixedSource, StringComparison.Ordinal);
        Assert.Equal(
            fixedSource.IndexOf("response = await client.SendAsync", StringComparison.Ordinal),
            fixedSource.LastIndexOf("response = await client.SendAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CodeFix_IsNotOfferedForNestedAssignment()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task UseAsync(HttpClient client, HttpRequestMessage request, bool send, CancellationToken cancellationToken)
                {
                    HttpResponseMessage response;
                    if (send)
                    {
                        response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        _ = await response.Content.ReadAsStringAsync(cancellationToken);
                    }
                }
            }
            """;

        var titles = await CodeFixVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer, HCR060_DisposeResponseCodeFixProvider>
            .GetCodeFixTitlesAsync(source);

        Assert.Empty(titles);
    }

    [Fact]
    public async Task CodeFix_DisposesSynchronousResponseHeadersReadResult()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;

            public sealed class Client
            {
                public void Send(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var response = client.Send(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    _ = response.Content;
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer, HCR060_DisposeResponseCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "using var response = client.Send(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);",
            fixedSource,
            StringComparison.Ordinal);
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n");
    }
}
