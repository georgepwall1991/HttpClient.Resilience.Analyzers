using HttpClient.Resilience.Analyzers.Analyzers.Lifetime;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.Lifetime;

public sealed class HCR006_InvalidHttpClientTimeoutAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenTimeoutIsTimeSpanZero()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClient Create()
                {
                    var client = new HttpClient();
                    client.Timeout = TimeSpan.Zero;
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR006, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTimeoutIsNegativeFactoryResult()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClient Create()
                {
                    var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(-5);
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR006, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTimeoutIsDefault()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClient Create()
                {
                    var client = new HttpClient();
                    client.Timeout = default;
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR006, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTimeoutIsZeroInObjectInitializer()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClient Create()
                {
                    return new HttpClient { Timeout = new TimeSpan(0) };
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR006, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenTimeoutIsPositive()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClient Create()
                {
                    var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(30);
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenTimeoutIsInfiniteTimeSpan()
    {
        const string source = """
            using System.Net.Http;
            using System.Threading;

            public sealed class Factory
            {
                public HttpClient Create()
                {
                    var client = new HttpClient();
                    client.Timeout = Timeout.InfiniteTimeSpan;
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenTimeoutIsNonConstant()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClient Create(TimeSpan timeout)
                {
                    var client = new HttpClient();
                    client.Timeout = timeout;
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenCustomTimeoutPropertyIsAssignedZero()
    {
        const string source = """
            using System;

            public sealed class CustomClient
            {
                public TimeSpan Timeout { get; set; }
            }

            public sealed class Factory
            {
                public CustomClient Create()
                {
                    var client = new CustomClient();
                    client.Timeout = TimeSpan.Zero;
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }
}
