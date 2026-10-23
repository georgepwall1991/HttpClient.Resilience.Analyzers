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
    [Fact]
    public async Task DoesNotReport_WhenMultiArgumentTimeSpanIsPositive()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClient Create()
                {
                    var client = new HttpClient();
                    client.Timeout = new TimeSpan(0, 1, 0);
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenTimeoutIsInfiniteViaFromMilliseconds()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClient Create()
                {
                    var client = new HttpClient();
                    client.Timeout = TimeSpan.FromMilliseconds(-1);
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenMultiArgumentTimeSpanIsZero()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClient Create()
                {
                    var client = new HttpClient();
                    client.Timeout = new TimeSpan(0, 0, 0);
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR006, diagnostic.Id);
    }


    [Fact]
    public async Task ReportsDiagnostic_ForEveryNonPositiveTimeSpanShape()
    {
        var assignments = new[]
        {
            "client.Timeout = TimeSpan.Zero;",
            "client.Timeout = default(TimeSpan);",
            "client.Timeout = default;",
            "client.Timeout = new TimeSpan();",
            "client.Timeout = new TimeSpan(0);",
            "client.Timeout = new TimeSpan(0L);",
            "client.Timeout = new TimeSpan(0, 0, 0);",
            "client.Timeout = new TimeSpan(0, 0, 0, 0);",
            "client.Timeout = new TimeSpan(0, 0, 0, 0, 0);",
            "client.Timeout = new TimeSpan(0, 0, 0, 0, 0, 0);",
            "client.Timeout = new TimeSpan(-1);",
            "client.Timeout = new TimeSpan(0, 0, -1);",
            "client.Timeout = new TimeSpan(0, 0, 0, -1);",
            "client.Timeout = new TimeSpan(0, 0, 0, 0, -2);",
            "client.Timeout = new TimeSpan(0, 0, 0, 0, 0, -1);",
            "client.Timeout = TimeSpan.FromDays(0);",
            "client.Timeout = TimeSpan.FromHours(0);",
            "client.Timeout = TimeSpan.FromMinutes(0);",
            "client.Timeout = TimeSpan.FromSeconds(0);",
            "client.Timeout = TimeSpan.FromMilliseconds(0);",
            "client.Timeout = TimeSpan.FromTicks(0);",
            "client.Timeout = TimeSpan.FromMicroseconds(0);",
            "client.Timeout = TimeSpan.FromSeconds(0L);",
            "client.Timeout = TimeSpan.FromSeconds(0.0);",
            "client.Timeout = TimeSpan.FromSeconds(0f);",
            "client.Timeout = TimeSpan.FromSeconds((short)0);",
            "client.Timeout = TimeSpan.FromSeconds((byte)0);",
            "client.Timeout = TimeSpan.FromSeconds(-5);",
            "client.Timeout = TimeSpan.FromMilliseconds(-2);",
            "client.Timeout = TimeSpan.FromTicks(-10001);",
            "client.Timeout = TimeSpan.FromMicroseconds(-1001);",
            "client.Timeout = -TimeSpan.FromSeconds(0);",
            "client.Timeout = -TimeSpan.FromTicks(0);",
            "client.Timeout = -TimeSpan.FromSeconds(5);",
            "client.Timeout = -TimeSpan.FromTicks(1);",
            "client.Timeout = -(TimeSpan.FromSeconds(5));",
            "client.Timeout = System.TimeSpan.Zero;",
            "client.Timeout = global::System.TimeSpan.Zero;",
            "client.Timeout = default(System.TimeSpan);",
            "client.Timeout = new System.TimeSpan(0);"
        };

        foreach (var assignment in assignments)
        {
            var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>.GetDiagnosticsAsync(
                BuildSource(assignment));

            Assert.True(
                diagnostics.Any(diagnostic => diagnostic.Id == DiagnosticIds.HCR006),
                $"Expected HCR006 for: {assignment}");
        }
    }

    [Fact]
    public async Task DoesNotReport_ForEverySafeTimeSpanShape()
    {
        var assignments = new[]
        {
            "client.Timeout = TimeSpan.FromSeconds(30);",
            "client.Timeout = TimeSpan.FromMilliseconds(-1);",
            "client.Timeout = TimeSpan.FromMicroseconds(-1000);",
            "client.Timeout = TimeSpan.FromTicks(-10000);",
            "client.Timeout = Timeout.InfiniteTimeSpan;",
            "client.Timeout = new TimeSpan(0, 1, 0);",
            "client.Timeout = new TimeSpan(1, 0, 0);",
            "client.Timeout = new TimeSpan(0, 1, 0, 0);",
            "client.Timeout = new TimeSpan(0, 0, 1, 0);",
            "client.Timeout = new TimeSpan(0, 0, 0, 1);",
            "client.Timeout = new TimeSpan(1, 0, 0, 0);",
            "client.Timeout = new TimeSpan(0, 1, 0, 0, 0);",
            "client.Timeout = new TimeSpan(0, 0, 1, 0, 0);",
            "client.Timeout = new TimeSpan(0, 0, 0, 1, 0);",
            "client.Timeout = new TimeSpan(0, 0, 0, 0, 1);",
            "client.Timeout = new TimeSpan(0, 0, 0, 0, -1);",
            "client.Timeout = new TimeSpan(1, 0, 0, 0, 0);",
            "client.Timeout = new TimeSpan(0, 1, 0, 0, 0, 0);",
            "client.Timeout = new TimeSpan(0, 0, 1, 0, 0, 0);",
            "client.Timeout = new TimeSpan(0, 0, 0, 1, 0, 0);",
            "client.Timeout = new TimeSpan(0, 0, 0, 0, 1, 0);",
            "client.Timeout = new TimeSpan(0, 0, 0, 0, 0, 1);",
            "client.Timeout = new TimeSpan(0, 0, 0, 0, 0, -1000);",
            "client.Timeout = TimeSpan.FromDays(1);",
            "client.Timeout = TimeSpan.FromHours(1);",
            "client.Timeout = TimeSpan.FromMinutes(1);",
            "client.Timeout = TimeSpan.FromSeconds(1);",
            "client.Timeout = TimeSpan.FromMilliseconds(1);",
            "client.Timeout = TimeSpan.FromTicks(1);",
            "client.Timeout = TimeSpan.FromMicroseconds(1);",
            "client.Timeout = -TimeSpan.Parse(\"00:00:05\");",
            "client.Timeout = -TimeSpan.Parse(\"5\");",
            "client.Timeout = -TimeSpan.FromMilliseconds(1);",
            "client.Timeout = -TimeSpan.FromTicks(10000);",
            "client.Timeout = -TimeSpan.FromMicroseconds(1000);",
            "client.Timeout = -TimeSpan.FromSeconds(-5);",
            "client.Timeout = -TimeSpan.FromMilliseconds(-1);"
        };

        foreach (var assignment in assignments)
        {
            var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>.GetDiagnosticsAsync(
                BuildSource(assignment));

            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == DiagnosticIds.HCR006);
        }
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenUnresolvedReceiverAndValueLookLikeHttpClientAndTimeSpan()
    {
        const string source = """
            public sealed class Factory
            {
                public void Configure(HttpClient client)
                {
                    client.Timeout = TimeSpan.Zero;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR006, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenUnresolvedObjectInitializerLooksLikeHttpClient()
    {
        const string source = """
            public sealed class Factory
            {
                public object Create()
                {
                    return new HttpClient { Timeout = TimeSpan.Zero };
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR006, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnresolvedLeftIsConditionalAccess()
    {
        const string source = """
            public sealed class Factory
            {
                public void Configure(HttpClient client)
                {
                    client?.Timeout = TimeSpan.Zero;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenUnresolvedTimeSpanCreationHasZeroTicks()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Factory
            {
                public void Configure(HttpClient client)
                {
                    client.Timeout = new TimeSpan(0);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR006, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnresolvedReceiverIsNotHttpClient()
    {
        const string source = """
            public sealed class Factory
            {
                public void Configure(CustomClient client)
                {
                    client.Timeout = TimeSpan.Zero;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnresolvedMemberIsNotTimeout()
    {
        const string source = """
            public sealed class Factory
            {
                public void Configure(HttpClient client)
                {
                    client.BaseAddress = TimeSpan.Zero;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnresolvedObjectInitializerIsNotHttpClient()
    {
        const string source = """
            public sealed class Factory
            {
                public object Create()
                {
                    return new CustomClient { Timeout = TimeSpan.Zero };
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenUnresolvedQualifiedTimeSpanIsCreated()
    {
        const string source = """
            public sealed class Factory
            {
                public void Configure(HttpClient client)
                {
                    client.Timeout = new Unknown.TimeSpan(0);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR006, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnresolvedQualifiedTypeIsNotTimeSpan()
    {
        const string source = """
            public sealed class Factory
            {
                public void Configure(HttpClient client)
                {
                    client.Timeout = new Unknown.NotTimeSpan(0);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnresolvedCreationHasUnsupportedArgumentCount()
    {
        const string source = """
            public sealed class Factory
            {
                public void Configure(HttpClient client)
                {
                    client.Timeout = new TimeSpan(0, 0);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnresolvedCreationArgumentIsNotConstant()
    {
        const string source = """
            public sealed class Factory
            {
                public void Configure(HttpClient client, long ticks)
                {
                    client.Timeout = new TimeSpan(ticks);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenUnresolvedFactoryHasStringLiteralZero()
    {
        const string source = """
            public sealed class Factory
            {
                public void Configure(HttpClient client)
                {
                    client.Timeout = TimeSpan.FromSeconds("0");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR006, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnresolvedFactoryHasInvalidStringLiteral()
    {
        const string source = """
            public sealed class Factory
            {
                public void Configure(HttpClient client)
                {
                    client.Timeout = TimeSpan.FromSeconds("not-a-number");
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnresolvedCreationQualifiedTypeIsNotTimeSpan()
    {
        const string source = """
            public sealed class Factory
            {
                public void Configure(HttpClient client)
                {
                    client.Timeout = default(Unknown.NotTimeSpan);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenUnresolvedDefaultQualifiedTypeIsTimeSpan()
    {
        const string source = """
            public sealed class Factory
            {
                public void Configure(HttpClient client)
                {
                    client.Timeout = default(Unknown.TimeSpan);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR006, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenTimeSpanNameBelongsToAnotherType()
    {
        const string source = """
            using System.Net.Http;

            public sealed class TimeSpan
            {
                public static TimeSpan Zero { get; } = new TimeSpan(0);

                public TimeSpan(int ticks)
                {
                }

                public static implicit operator global::System.TimeSpan(TimeSpan value) => default;
            }

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

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenCreationTypeNameBelongsToAnotherType()
    {
        const string source = """
            using System.Net.Http;

            public sealed class TimeSpan
            {
                public TimeSpan(int ticks)
                {
                }

                public static implicit operator global::System.TimeSpan(TimeSpan value) => default;
            }

            public sealed class Factory
            {
                public HttpClient Create()
                {
                    var client = new HttpClient();
                    client.Timeout = new TimeSpan(0);
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenCustomQualifiedTimeSpanIsCreated()
    {
        const string source = """
            using System.Net.Http;

            namespace Custom
            {
                public sealed class TimeSpan
                {
                    public TimeSpan(int ticks)
                    {
                    }

                    public static implicit operator global::System.TimeSpan(TimeSpan value) => default;
                }
            }

            public sealed class Factory
            {
                public HttpClient Create()
                {
                    var client = new HttpClient();
                    client.Timeout = new global::Custom.TimeSpan(0);
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenAliasQualifiedTypeNameIsNotTimeSpan()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClient Create()
                {
                    var client = new HttpClient();
                    client.Timeout = new global::TimeSpan(0);
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnresolvedFactoryHasNoArguments()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Factory
            {
                public void Configure(HttpClient client)
                {
                    client.Timeout = -TimeSpan.FromSeconds();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnresolvedFactoryHasNoArgumentsWithoutUnaryOperator()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Factory
            {
                public void Configure(HttpClient client)
                {
                    client.Timeout = TimeSpan.FromSeconds();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnresolvedIdentifierTypeIsNotTimeSpan()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Factory
            {
                public void Configure(HttpClient client)
                {
                    client.Timeout = new NotTimeSpan(0);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenUnresolvedSystemAliasMakesTimeSpanLookQualified()
    {
        const string source = """
            using System = DoesNotExist;

            public sealed class Factory
            {
                public void Configure(global::System.Net.Http.HttpClient client)
                {
                    client.Timeout = System.TimeSpan.Zero;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR006_InvalidHttpClientTimeoutAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR006, diagnostic.Id);
    }

    private static string BuildSource(string statement)
    {
        return $$"""
            using System;
            using System.Net.Http;
            using System.Threading;

            public sealed class Factory
            {
                public HttpClient Create(TimeSpan timeout)
                {
                    var client = new HttpClient();
                    {{statement}}
                    return client;
                }
            }
            """;
    }
}
