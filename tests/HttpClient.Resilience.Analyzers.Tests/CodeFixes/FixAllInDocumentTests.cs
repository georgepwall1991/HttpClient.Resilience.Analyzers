using System.Threading.Tasks;
using HttpClient.Resilience.Analyzers.Analyzers.Lifetime;
using HttpClient.Resilience.Analyzers.Analyzers.ResponseLifetime;
using HttpClient.Resilience.Analyzers.CodeFixes;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;
using Xunit;

namespace HttpClient.Resilience.Analyzers.Tests.CodeFixes;

/// <summary>
/// End-to-end Fix All verification: several diagnostics of one id in a single document must
/// all be fixed by BatchFixer and leave no remaining analyzer diagnostics.
/// </summary>
public sealed class FixAllInDocumentTests
{
    [Fact]
    public async Task HCR001_FixAllReplacesEveryManualClientCreation()
    {
        const string source = """
            using System.Net.Http;

            public sealed class PaymentsService
            {
                public IHttpClientFactory HttpClientFactory { get; init; }

                public HttpClient CreatePrimary()
                {
                    return new HttpClient();
                }

                public HttpClient CreateSecondary()
                {
                    return new HttpClient();
                }
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name = "");
            }
            """;

        var diagnosticsBefore = await AnalyzerVerifier<HCR001_NewHttpClientInRequestPathAnalyzer>.GetDiagnosticsAsync(source);
        Assert.Equal(2, diagnosticsBefore.Length);

        var fixedSource = await CodeFixVerifier<HCR001_NewHttpClientInRequestPathAnalyzer, HCR001_UseHttpClientFactoryCodeFixProvider>
                    .ApplyFixAllInDocumentAsync(source);

        Assert.DoesNotContain("new HttpClient()", fixedSource);
        Assert.Contains("HttpClientFactory.CreateClient();", fixedSource, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task HCR060_FixAllConvertsEveryResponseDeclarationToUsing()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task UseAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    var first = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    _ = await first.Content.ReadAsStringAsync(cancellationToken);
                    var second = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    _ = await second.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            """;

        var diagnosticsBefore = await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(source);
        Assert.Equal(2, diagnosticsBefore.Length);

        var fixedSource = await CodeFixVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer, HCR060_DisposeResponseCodeFixProvider>
                    .ApplyFixAllInDocumentAsync(source);

        Assert.Empty(await AnalyzerVerifier<HCR060_ResponseHeadersReadDisposalAnalyzer>.GetDiagnosticsAsync(fixedSource));
        Assert.Contains("using var first =", fixedSource, System.StringComparison.Ordinal);
        Assert.Contains("using var second =", fixedSource, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task HCR064_FixAllPassesTokenToEveryUnguardedCall()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<string> UseAsync(HttpClient client, CancellationToken cancellationToken)
                {
                    var first = await client.GetStringAsync("https://example.com/primary");
                    var second = await client.GetStringAsync("https://example.com/secondary");
                    return first + second;
                }
            }
            """;

        var diagnosticsBefore = await AnalyzerVerifier<HCR064_CancellationAwareHttpAnalyzer>.GetDiagnosticsAsync(source);
        Assert.Equal(2, diagnosticsBefore.Length);

        var fixedSource = await CodeFixVerifier<HCR064_CancellationAwareHttpAnalyzer, HCR064_PassCancellationTokenCodeFixProvider>
                    .ApplyFixAllInDocumentAsync(source);

        Assert.Empty(await AnalyzerVerifier<HCR064_CancellationAwareHttpAnalyzer>.GetDiagnosticsAsync(fixedSource));
        Assert.Contains(
            "client.GetStringAsync(\"https://example.com/primary\", cancellationToken: cancellationToken)",
            fixedSource,
            System.StringComparison.Ordinal);
        Assert.Contains(
            "client.GetStringAsync(\"https://example.com/secondary\", cancellationToken: cancellationToken)",
            fixedSource,
            System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task HCR063_FixAllAwaitsEveryBlockingResult()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<int> UseAsync(HttpClient client)
                {
                    var primary = client.GetAsync("https://example.com/primary").Result;
                    var secondary = client.GetAsync("https://example.com/secondary").Result;
                    return primary.StatusCode.GetHashCode() + secondary.StatusCode.GetHashCode();
                }
            }
            """;

        var diagnosticsBefore = await AnalyzerVerifier<HCR063_SyncOverAsyncHttpAnalyzer>.GetDiagnosticsAsync(source);
        Assert.Equal(2, diagnosticsBefore.Length);

        var fixedSource = await CodeFixVerifier<HCR063_SyncOverAsyncHttpAnalyzer, HCR063_AwaitHttpOperationCodeFixProvider>
                    .ApplyFixAllInDocumentAsync(source);

        Assert.DoesNotContain(".Result", fixedSource);
        Assert.Contains("await client.GetAsync(\"https://example.com/primary\")", fixedSource, System.StringComparison.Ordinal);
        Assert.Contains("await client.GetAsync(\"https://example.com/secondary\")", fixedSource, System.StringComparison.Ordinal);
    }
}
