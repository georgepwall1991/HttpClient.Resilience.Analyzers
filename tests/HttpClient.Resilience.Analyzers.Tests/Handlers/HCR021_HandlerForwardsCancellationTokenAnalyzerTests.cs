using HttpClient.Resilience.Analyzers.Analyzers.Handlers;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.Handlers;

public sealed class HCR021_HandlerForwardsCancellationTokenAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenBaseSendAsyncPassesCancellationTokenNone()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class LoggingHandler : DelegatingHandler
            {
                protected override Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request,
                    CancellationToken cancellationToken)
                {
                    return base.SendAsync(request, CancellationToken.None);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR021_HandlerForwardsCancellationTokenAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR021, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenBaseSendAsyncPassesDefaultToken()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class LoggingHandler : DelegatingHandler
            {
                protected override Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request,
                    CancellationToken cancellationToken)
                {
                    return base.SendAsync(request, default);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR021_HandlerForwardsCancellationTokenAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR021, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenBaseSendAsyncPassesNewToken()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class LoggingHandler : DelegatingHandler
            {
                protected override Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request,
                    CancellationToken cancellationToken)
                {
                    return base.SendAsync(request, new CancellationToken());
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR021_HandlerForwardsCancellationTokenAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR021, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenBaseSendAsyncForwardsTheToken()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class LoggingHandler : DelegatingHandler
            {
                protected override Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request,
                    CancellationToken cancellationToken)
                {
                    return base.SendAsync(request, cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR021_HandlerForwardsCancellationTokenAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenBaseSendAsyncForwardsLinkedToken()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class LoggingHandler : DelegatingHandler
            {
                protected override async Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request,
                    CancellationToken cancellationToken)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    return await base.SendAsync(request, timeout.Token);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR021_HandlerForwardsCancellationTokenAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenSendAsyncIsNotAnOverride()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class NotAHandler
            {
                public Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request,
                    CancellationToken cancellationToken)
                {
                    return Task.FromResult(new HttpResponseMessage());
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR021_HandlerForwardsCancellationTokenAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenHandlerDoesNotDeriveFromDelegatingHandler()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public class CustomBase
            {
                public virtual Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request,
                    CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage());
            }

            public sealed class CustomHandler : CustomBase
            {
                public override Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request,
                    CancellationToken cancellationToken)
                {
                    return base.SendAsync(request, CancellationToken.None);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR021_HandlerForwardsCancellationTokenAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenHandlerDerivesFromCustomDelegatingHandler()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public abstract class BaseHandler : DelegatingHandler
            {
            }

            public sealed class LoggingHandler : BaseHandler
            {
                protected override Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request,
                    CancellationToken cancellationToken)
                {
                    return base.SendAsync(request, CancellationToken.None);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR021_HandlerForwardsCancellationTokenAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR021, diagnostic.Id);
    }
}
