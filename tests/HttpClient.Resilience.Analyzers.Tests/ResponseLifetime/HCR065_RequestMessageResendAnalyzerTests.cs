using HttpClient.Resilience.Analyzers.Analyzers.ResponseLifetime;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.ResponseLifetime;

public sealed class HCR065_RequestMessageResendAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenSameRequestIsSentTwice()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, "/orders");
                    await client.SendAsync(request, cancellationToken);
                    await client.SendAsync(request, cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR065, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenRequestIsSentInsideRetryLoop()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class RetryHandler : DelegatingHandler
            {
                protected override async Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request,
                    CancellationToken cancellationToken)
                {
                    for (var attempt = 0; attempt < 3; attempt++)
                    {
                        var response = await base.SendAsync(request, cancellationToken);
                        if (response.IsSuccessStatusCode)
                        {
                            return response;
                        }
                    }

                    return new HttpResponseMessage();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR065, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenRequestIsReassignedBetweenSends()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, "/orders");
                    await client.SendAsync(request, cancellationToken);
                    request = new HttpRequestMessage(HttpMethod.Post, "/orders");
                    await client.SendAsync(request, cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenLoopAssignsFreshRequestEachIteration()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client, string[] urls, CancellationToken cancellationToken)
                {
                    HttpRequestMessage? request = null;
                    foreach (var url in urls)
                    {
                        request = new HttpRequestMessage(HttpMethod.Get, url);
                        await client.SendAsync(request, cancellationToken);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenDistinctRequestsAreSent()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/a"), cancellationToken);
                    await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/b"), cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenCustomSendAsyncLookalikeSendsTwice()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class CustomClient
            {
                public Task SendAsync(object request, CancellationToken cancellationToken) => Task.CompletedTask;
            }

            public sealed class Caller
            {
                public async Task SendAsync(CustomClient client, CancellationToken cancellationToken)
                {
                    var request = new object();
                    await client.SendAsync(request, cancellationToken);
                    await client.SendAsync(request, cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenRequestIsSentOnce()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, "/orders");
                    await client.SendAsync(request, cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }
    [Fact]
    public async Task DoesNotReport_WhenRequestIsDeclaredInsideLoop()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client, string[] urls, CancellationToken cancellationToken)
                {
                    foreach (var url in urls)
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, url);
                        await client.SendAsync(request, cancellationToken);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenRequestIsForEachIterationVariable()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client, HttpRequestMessage[] requests, CancellationToken cancellationToken)
                {
                    foreach (var request in requests)
                    {
                        await client.SendAsync(request, cancellationToken);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenRequestIsDeclaredInForLoopInitializer()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    for (var request = new HttpRequestMessage(HttpMethod.Get, "/orders"); cancellationToken.CanBeCanceled;)
                    {
                        await client.SendAsync(request, cancellationToken);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR065, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenRequestIsDeclaredInForLoopBody()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    for (var index = 0; index < 3; index++)
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, "/orders");
                        await client.SendAsync(request, cancellationToken);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }


    [Fact]
    public async Task DoesNotReport_WhenSendHasNoRequestArgument()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client)
                {
                    await client.Send();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenParameterRequestIsSentInsideForLoop()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    for (var attempt = 0; attempt < 3; attempt++)
                    {
                        await client.SendAsync(request, cancellationToken);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR065, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenNonSendMethodUsesTheSameRequestTwice()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public static class ClientExtensions
            {
                public static Task<HttpResponseMessage> Fetch(
                    this HttpClient client,
                    HttpRequestMessage request,
                    CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage());
            }

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, "/orders");
                    await client.Fetch(request, cancellationToken);
                    await client.Fetch(request, cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenHttpMessageInvokerSendsTheSameRequestTwice()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpMessageInvoker invoker, CancellationToken cancellationToken)
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, "/orders");
                    await invoker.SendAsync(request, cancellationToken);
                    await invoker.SendAsync(request, cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR065, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenDelegatingHandlerBaseSendIsRepeated()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Handler : DelegatingHandler
            {
                public Task<HttpResponseMessage> SendTwiceAsync(
                    HttpRequestMessage request,
                    CancellationToken cancellationToken)
                {
                    var first = base.SendAsync(request, cancellationToken);
                    var second = base.SendAsync(request, cancellationToken);
                    return first;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR065, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenUnresolvedBaseSendIsRepeated()
    {
        const string source = """
            public sealed class Handler
            {
                protected override object SendAsync(object request, object cancellationToken)
                {
                    var first = base.SendAsync(request, cancellationToken);
                    var second = base.SendAsync(request, cancellationToken);
                    return first;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR065, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenUnresolvedReceiverLooksLikeHttpClient()
    {
        const string source = """
            public sealed class Client
            {
                public void SendAsync(HttpClient client, HttpRequestMessage request, object cancellationToken)
                {
                    var first = client.SendAsync(request, cancellationToken);
                    var second = client.SendAsync(request, cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR065, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenUnresolvedQualifiedReceiverLooksLikeHttpClient()
    {
        const string source = """
            public sealed class Client
            {
                public void SendAsync(
                    global::System.Net.Http.HttpClient client,
                    global::System.Net.Http.HttpRequestMessage request,
                    object cancellationToken)
                {
                    var first = client.SendAsync(request, cancellationToken);
                    var second = client.SendAsync(request, cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR065, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnresolvedReceiverIsNotHttpClient()
    {
        const string source = """
            public sealed class Client
            {
                public void SendAsync(CustomClient client, HttpRequestMessage request, object cancellationToken)
                {
                    var first = client.SendAsync(request, cancellationToken);
                    var second = client.SendAsync(request, cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenRequestLocalIsDeclaredOutsideLoop()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, "/orders");
                    for (var attempt = 0; attempt < 3; attempt++)
                    {
                        await client.SendAsync(request, cancellationToken);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR065, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsOnlyLoopDiagnostic_WhenSameRequestIsSentAfterLoop()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task SendAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, "/orders");
                    for (var attempt = 0; attempt < 3; attempt++)
                    {
                        await client.SendAsync(request, cancellationToken);
                    }

                    await client.SendAsync(request, cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR065, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenSendIsRepeated()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public void Send(HttpClient client, CancellationToken cancellationToken)
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, "/orders");
                    var first = client.Send(request, cancellationToken);
                    var second = client.Send(request, cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR065, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenCustomClientHasParameterlessSend()
    {
        const string source = """
            public sealed class HttpClient
            {
                public void Send()
                {
                }
            }

            public sealed class Client
            {
                public void Configure(HttpClient client)
                {
                    client.Send();
                    client.Send();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenCustomClientLooksLikeHttpClient()
    {
        const string source = """
            public sealed class HttpClient
            {
                public void SendAsync(global::System.Net.Http.HttpRequestMessage request)
                {
                }
            }

            public sealed class Client
            {
                public void Configure(HttpClient client, global::System.Net.Http.HttpRequestMessage request)
                {
                    client.SendAsync(request);
                    client.SendAsync(request);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenCustomHandlerLooksLikeHttpMessageHandler()
    {
        const string source = """
            public abstract class HttpMessageHandler
            {
            }

            public sealed class CustomHandler : HttpMessageHandler
            {
                public void SendAsync(global::System.Net.Http.HttpRequestMessage request)
                {
                }
            }

            public sealed class Client
            {
                public void Configure(CustomHandler handler, global::System.Net.Http.HttpRequestMessage request)
                {
                    handler.SendAsync(request);
                    handler.SendAsync(request);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenUnresolvedSimpleHttpMessageInvokerReceiverIsReused()
    {
        const string source = """
            public sealed class Client
            {
                public void SendAsync(HttpMessageInvoker invoker, HttpRequestMessage request, object cancellationToken)
                {
                    var first = invoker.SendAsync(request, cancellationToken);
                    var second = invoker.SendAsync(request, cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR065, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenUnresolvedQualifiedHttpMessageInvokerReceiverIsReused()
    {
        const string source = """
            public sealed class Client
            {
                public void SendAsync(
                    global::System.Net.Http.HttpMessageInvoker invoker,
                    global::System.Net.Http.HttpRequestMessage request,
                    object cancellationToken)
                {
                    var first = invoker.SendAsync(request, cancellationToken);
                    var second = invoker.SendAsync(request, cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR065, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnresolvedReceiverTypeSyntaxIsNotHttpClientName()
    {
        const string source = """
            public sealed class Client
            {
                public void SendAsync(CustomClient[] client, HttpRequestMessage request, object cancellationToken)
                {
                    var first = client.SendAsync(request, cancellationToken);
                    var second = client.SendAsync(request, cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR065_RequestMessageResendAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Empty(diagnostics);
    }
}
