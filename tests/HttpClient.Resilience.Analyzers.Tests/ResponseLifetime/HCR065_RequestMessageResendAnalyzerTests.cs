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
}
