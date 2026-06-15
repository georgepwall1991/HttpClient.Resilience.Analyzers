using HttpClient.Resilience.Analyzers.Analyzers.ResponseLifetime;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.ResponseLifetime;

public sealed class HCR081_HttpStreamDisposalAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenHttpContentStreamIsNotDisposed()
    {
        const string source = """
            using System.IO;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task CopyAsync(HttpResponseMessage response, Stream destination, CancellationToken cancellationToken)
                {
                    var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await stream.CopyToAsync(destination, cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR081_HttpStreamDisposalAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR081, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenHttpClientGetStreamResultIsNotDisposed()
    {
        const string source = """
            using System.IO;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task CopyAsync(HttpClient client, Stream destination, CancellationToken cancellationToken)
                {
                    var stream = await client.GetStreamAsync("https://example.com", cancellationToken);
                    await stream.CopyToAsync(destination, cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR081_HttpStreamDisposalAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR081, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenExistingStreamLocalIsAssigned()
    {
        const string source = """
            using System.IO;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task CopyAsync(HttpResponseMessage response, Stream destination, CancellationToken cancellationToken)
                {
                    Stream stream;
                    stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await stream.CopyToAsync(destination, cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR081_HttpStreamDisposalAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR081, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenStreamUsesUsingDeclaration()
    {
        const string source = """
            using System.IO;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task CopyAsync(HttpResponseMessage response, Stream destination, CancellationToken cancellationToken)
                {
                    using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await stream.CopyToAsync(destination, cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR081_HttpStreamDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenUsingStatementOwnsStream()
    {
        const string source = """
            using System.IO;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task CopyAsync(HttpResponseMessage response, Stream destination, CancellationToken cancellationToken)
                {
                    var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using (stream)
                    {
                        await stream.CopyToAsync(destination, cancellationToken);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR081_HttpStreamDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenStreamIsDisposedAsync()
    {
        const string source = """
            using System.IO;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task CopyAsync(HttpResponseMessage response, Stream destination, CancellationToken cancellationToken)
                {
                    var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await stream.CopyToAsync(destination, cancellationToken);
                    await stream.DisposeAsync();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR081_HttpStreamDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenStreamIsReturned()
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
                    var stream = await client.GetStreamAsync("https://example.com", cancellationToken);
                    return stream;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR081_HttpStreamDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenStreamIsTransferredToReturnedOwner()
    {
        const string source = """
            using System.IO;
            using System.Net.Http;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task<StreamOwner> OpenAsync(HttpResponseMessage response, CancellationToken cancellationToken)
                {
                    var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    return new StreamOwner(stream);
                }
            }

            public sealed class StreamOwner(Stream stream)
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR081_HttpStreamDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResolvedCustomHttpClientGetStreamAsyncIsUsed()
    {
        const string source = """
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Client
            {
                public async Task CopyAsync(Custom.HttpClient client, Stream destination, CancellationToken cancellationToken)
                {
                    var stream = await client.GetStreamAsync("https://example.com", cancellationToken);
                    await stream.CopyToAsync(destination, cancellationToken);
                }
            }

            namespace Custom
            {
                public sealed class HttpClient
                {
                    public Task<Stream> GetStreamAsync(string url, CancellationToken cancellationToken)
                    {
                        return Task.FromResult(Stream.Null);
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR081_HttpStreamDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }
}
