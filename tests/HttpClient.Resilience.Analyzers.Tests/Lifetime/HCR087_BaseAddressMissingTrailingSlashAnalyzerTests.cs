using HttpClient.Resilience.Analyzers.Analyzers.Lifetime;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.Lifetime;

public sealed class HCR087_BaseAddressMissingTrailingSlashAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenBaseAddressPathLacksTrailingSlash()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClient Create()
                {
                    var client = new HttpClient();
                    client.BaseAddress = new Uri("https://api.example.com/v1");
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR087_BaseAddressMissingTrailingSlashAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR087, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenBaseAddressIsStringLiteral()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClient Create()
                {
                    var client = new HttpClient();
                    client.BaseAddress = new System.Uri("https://api.example.com/v1");
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR087_BaseAddressMissingTrailingSlashAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR087, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenBaseAddressEndsWithSlash()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClient Create()
                {
                    var client = new HttpClient();
                    client.BaseAddress = new Uri("https://api.example.com/v1/");
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR087_BaseAddressMissingTrailingSlashAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenBaseAddressIsRootOnly()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClient Create()
                {
                    var client = new HttpClient();
                    client.BaseAddress = new Uri("https://api.example.com");
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR087_BaseAddressMissingTrailingSlashAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenBaseAddressIsNonConstant()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClient Create(Uri baseAddress)
                {
                    var client = new HttpClient();
                    client.BaseAddress = baseAddress;
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR087_BaseAddressMissingTrailingSlashAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenBaseAddressIsOnCustomType()
    {
        const string source = """
            using System;

            public sealed class CustomClient
            {
                public Uri BaseAddress { get; set; }
            }

            public sealed class Factory
            {
                public CustomClient Create()
                {
                    var client = new CustomClient();
                    client.BaseAddress = new Uri("https://api.example.com/v1");
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR087_BaseAddressMissingTrailingSlashAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }
    [Fact]
    public async Task ReportsDiagnostic_WhenBaseAddressIsSetInObjectInitializer()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClient Create()
                {
                    return new HttpClient
                    {
                        BaseAddress = new Uri("https://api.example.com/v1")
                    };
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR087_BaseAddressMissingTrailingSlashAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR087, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenBaseAddressUsesTargetTypedNew()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClient Create()
                {
                    HttpClient client = new()
                    {
                        BaseAddress = new("https://api.example.com/v1")
                    };
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR087_BaseAddressMissingTrailingSlashAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR087, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenBaseAddressComesFromConstant()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class Factory
            {
                private const string BaseUrl = "https://api.example.com/v1";

                public HttpClient Create()
                {
                    var client = new HttpClient();
                    client.BaseAddress = new Uri(BaseUrl);
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR087_BaseAddressMissingTrailingSlashAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR087, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenUriKindIsSpecified()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClient Create()
                {
                    var client = new HttpClient();
                    client.BaseAddress = new Uri("https://api.example.com/v1", UriKind.Absolute);
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR087_BaseAddressMissingTrailingSlashAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR087, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenUriKindIsRelative()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClient Create()
                {
                    var client = new HttpClient();
                    client.BaseAddress = new Uri("api/v1", UriKind.Relative);
                    return client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR087_BaseAddressMissingTrailingSlashAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenObjectInitializerIsOnCustomType()
    {
        const string source = """
            using System;

            public sealed class CustomClient
            {
                public Uri BaseAddress { get; set; }
            }

            public sealed class Factory
            {
                public CustomClient Create()
                {
                    return new CustomClient
                    {
                        BaseAddress = new Uri("https://api.example.com/v1")
                    };
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR087_BaseAddressMissingTrailingSlashAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenObjectInitializerEndsWithSlash()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClient Create()
                {
                    return new HttpClient
                    {
                        BaseAddress = new Uri("https://api.example.com/v1/")
                    };
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR087_BaseAddressMissingTrailingSlashAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

}
