using HttpClient.Resilience.Analyzers.Analyzers.Handlers;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.Handlers;

public sealed class HCR020_DelegatingHandlerCapturesScopedDataAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenDelegatingHandlerCapturesHttpContextAccessor()
    {
        const string source = """
            using System.Net.Http;

            public sealed class UserHeaderHandler(IHttpContextAccessor accessor) : DelegatingHandler
            {
            }

            public interface IHttpContextAccessor
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR020_DelegatingHandlerCapturesScopedDataAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR020, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenDelegatingHandlerHasStatelessDependency()
    {
        const string source = """
            using System.Net.Http;

            public sealed class ApiKeyHandler(IApiKeyProvider provider) : DelegatingHandler
            {
            }

            public interface IApiKeyProvider
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR020_DelegatingHandlerCapturesScopedDataAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }
}
