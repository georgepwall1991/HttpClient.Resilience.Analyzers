using HttpClient.Resilience.Analyzers.Analyzers.ResponseLifetime;
using HttpClient.Resilience.Analyzers.CodeFixes;
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
    public async Task DoesNotReport_WhenStreamAliasIsDisposed()
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
                    var ownedStream = stream;
                    await ownedStream.CopyToAsync(destination, cancellationToken);
                    ownedStream.Dispose();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR081_HttpStreamDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenChainedStreamAliasUsesUsingDeclaration()
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
                    var intermediate = stream;
                    using var ownedStream = intermediate;
                    await ownedStream.CopyToAsync(destination, cancellationToken);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR081_HttpStreamDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenStreamAliasIsReassignedBeforeDisposal()
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
                    var ownedStream = stream;
                    ownedStream = Stream.Null;
                    await ownedStream.CopyToAsync(destination, cancellationToken);
                    ownedStream.Dispose();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR081_HttpStreamDisposalAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR081, diagnostic.Id);
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
    public async Task DoesNotReport_WhenStreamAliasIsReturned()
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
                    var result = stream;
                    return result;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR081_HttpStreamDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenReturnedOwnerReceivesStreamAlias()
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
                    var ownedStream = stream;
                    return new StreamOwner(ownedStream);
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
    public async Task DoesNotReport_WhenReturnedLocalOwnerReceivesStreamAlias()
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
                    var ownedStream = stream;
                    var owner = new StreamOwner(ownedStream);
                    return owner;
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
    public async Task ReportsDiagnostic_WhenReturnedStreamAliasWasReassigned()
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
                    var result = stream;
                    result = Stream.Null;
                    return result;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR081_HttpStreamDisposalAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR081, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenReturnedStreamOwnerWasReassigned()
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
                    var owner = new StreamOwner(stream);
                    owner = new StreamOwner(Stream.Null);
                    return owner;
                }
            }

            public sealed class StreamOwner(Stream stream)
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR081_HttpStreamDisposalAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR081, diagnostic.Id);
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

    [Fact]
    public async Task DoesNotReport_WhenCustomExtensionOnHttpClientReturnsStream()
    {
        const string source = """
            using System.IO;
            using System.Net.Http;
            using System.Threading.Tasks;

            public static class CustomExtensions
            {
                public static Task<Stream> GetStreamAsync(
                    this global::System.Net.Http.HttpClient client,
                    int key)
                {
                    return Task.FromResult<Stream>(new MemoryStream());
                }
            }

            public sealed class Client
            {
                public async Task CopyAsync(global::System.Net.Http.HttpClient client)
                {
                    var stream = await client.GetStreamAsync(42);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR081_HttpStreamDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenCustomExtensionOnHttpContentReturnsStream()
    {
        const string source = """
            using System.IO;
            using System.Net.Http;
            using System.Threading.Tasks;

            public static class CustomExtensions
            {
                public static Task<Stream> ReadAsStreamAsync(
                    this global::System.Net.Http.HttpContent content,
                    int key)
                {
                    return Task.FromResult<Stream>(new MemoryStream());
                }
            }

            public sealed class Client
            {
                public async Task CopyAsync(HttpResponseMessage response)
                {
                    var stream = await response.Content.ReadAsStreamAsync(42);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR081_HttpStreamDisposalAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task CodeFix_DisposesHttpContentStreamWithUsingDeclaration()
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

        var fixedSource = await CodeFixVerifier<HCR081_HttpStreamDisposalAnalyzer, HCR081_DisposeStreamCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);",
            fixedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_MergesAdjacentDeclarationAndAssignment()
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

        var fixedSource = await CodeFixVerifier<HCR081_HttpStreamDisposalAnalyzer, HCR081_DisposeStreamCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);",
            fixedSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Stream stream;", fixedSource, StringComparison.Ordinal);
        Assert.Equal(
            fixedSource.IndexOf("stream = await response.Content.ReadAsStreamAsync", StringComparison.Ordinal),
            fixedSource.LastIndexOf("stream = await response.Content.ReadAsStreamAsync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CodeFix_IsNotOfferedForNestedAssignment()
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
                    if (destination.CanWrite)
                    {
                        stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                        await stream.CopyToAsync(destination, cancellationToken);
                    }
                }
            }
            """;

        var titles = await CodeFixVerifier<HCR081_HttpStreamDisposalAnalyzer, HCR081_DisposeStreamCodeFixProvider>
            .GetCodeFixTitlesAsync(source);

        Assert.Empty(titles);
    }

    [Fact]
    public async Task CodeFix_DisposesSynchronousHttpContentStream()
    {
        const string source = """
            using System.IO;
            using System.Net.Http;

            public sealed class Client
            {
                public void Copy(HttpResponseMessage response, Stream destination)
                {
                    var stream = response.Content.ReadAsStream();
                    stream.CopyTo(destination);
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR081_HttpStreamDisposalAnalyzer, HCR081_DisposeStreamCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains(
            "using var stream = response.Content.ReadAsStream();",
            fixedSource,
            StringComparison.Ordinal);
    }
}
