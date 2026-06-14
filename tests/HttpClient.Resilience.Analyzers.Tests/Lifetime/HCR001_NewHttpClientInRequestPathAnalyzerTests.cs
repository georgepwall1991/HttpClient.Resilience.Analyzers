using HttpClient.Resilience.Analyzers.Analyzers.Lifetime;
using HttpClient.Resilience.Analyzers.CodeFixes;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.Lifetime;

public sealed class HCR001_NewHttpClientInRequestPathAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenServiceCreatesHttpClientInMethod()
    {
        const string source = """
            using System.Net.Http;

            public sealed class PaymentsService
            {
                public HttpClient Create()
                {
                    return new HttpClient();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR001_NewHttpClientInRequestPathAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR001, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenHttpClientIsCreatedInLoop()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Net.Http;

            public sealed class Utility
            {
                public void Send(IEnumerable<string> urls)
                {
                    foreach (var url in urls)
                    {
                        _ = new HttpClient();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR001_NewHttpClientInRequestPathAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR001, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenHttpClientIsCreatedInTestType()
    {
        const string source = """
            using System.Net.Http;

            public sealed class PaymentsServiceTests
            {
                public HttpClient Create()
                {
                    return new HttpClient();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR001_NewHttpClientInRequestPathAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task CodeFix_UsesExistingHttpClientFactoryParameter()
    {
        const string source = """
            using System.Net.Http;

            public sealed class PaymentsService
            {
                public HttpClient Create(IHttpClientFactory httpClientFactory)
                {
                    return new HttpClient();
                }
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name = "");
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR001_NewHttpClientInRequestPathAnalyzer, HCR001_UseHttpClientFactoryCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains("return httpClientFactory.CreateClient();", fixedSource);
        Assert.DoesNotContain("new HttpClient()", fixedSource);
    }
}
