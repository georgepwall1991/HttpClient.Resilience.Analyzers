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
    public async Task ReportsDiagnostic_WhenTopLevelUsingDeclarationCreatesHttpClient()
    {
        const string source = """
            using System.Net.Http;

            using var client = new HttpClient();
            """;

        var diagnostics = await AnalyzerVerifier<HCR001_NewHttpClientInRequestPathAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR001, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTopLevelLoopCreatesHttpClient()
    {
        const string source = """
            using System.Net.Http;

            foreach (var url in new[] { "https://example.com" })
            {
                _ = new HttpClient();
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR001_NewHttpClientInRequestPathAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR001, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenMinimalApiEndpointCreatesHttpClient()
    {
        const string source = """
            using System;
            using System.Net.Http;

            var app = WebApplication.Create();

            app.MapPost("/payments", () =>
            {
                return new HttpClient();
            });

            public sealed class WebApplication
            {
                public static WebApplication Create() => new();
                public void MapPost(string pattern, Func<HttpClient> handler)
                {
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR001_NewHttpClientInRequestPathAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR001, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenNullForgivingMinimalApiEndpointCreatesHttpClient()
    {
        const string source = """
            using System;
            using System.Net.Http;

            var app = WebApplication.Create();

            app!.MapPost("/payments", () => new HttpClient());

            public sealed class WebApplication
            {
                public static WebApplication Create() => new();
                public void MapPost(string pattern, Func<HttpClient> handler)
                {
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR001_NewHttpClientInRequestPathAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR001, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenMinimalApiRouteGroupEndpointCreatesHttpClient()
    {
        const string source = """
            using System;
            using System.Net.Http;

            var app = WebApplication.Create();

            app.MapGroup("/api").MapPost("/payments", () =>
            {
                return new HttpClient();
            });

            public sealed class WebApplication
            {
                public static WebApplication Create() => new();
                public RouteGroupBuilder MapGroup(string prefix) => new();
            }

            public sealed class RouteGroupBuilder
            {
                public void MapPost(string pattern, Func<HttpClient> handler)
                {
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR001_NewHttpClientInRequestPathAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR001, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenMinimalApiRouteGroupVariableEndpointCreatesHttpClient()
    {
        const string source = """
            using System;
            using System.Net.Http;

            var app = WebApplication.Create();
            var group = app.MapGroup("/api");

            group.MapPost("/payments", () =>
            {
                return new HttpClient();
            });

            public sealed class WebApplication
            {
                public static WebApplication Create() => new();
                public RouteGroupBuilder MapGroup(string prefix) => new();
            }

            public sealed class RouteGroupBuilder
            {
                public void MapPost(string pattern, Func<HttpClient> handler)
                {
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR001_NewHttpClientInRequestPathAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR001, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenNullForgivingRouteGroupEndpointCreatesHttpClient()
    {
        const string source = """
            using System;
            using System.Net.Http;

            var app = WebApplication.Create();
            var group = app.MapGroup("/api");

            group!.MapPost("/payments", () => new HttpClient());

            public sealed class WebApplication
            {
                public static WebApplication Create() => new();
                public RouteGroupBuilder MapGroup(string prefix) => new();
            }

            public sealed class RouteGroupBuilder
            {
                public void MapPost(string pattern, Func<HttpClient> handler)
                {
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR001_NewHttpClientInRequestPathAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR001, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenMinimalApiRouteGroupVariableHasEndpointBuilderType()
    {
        const string source = """
            using System;
            using System.Net.Http;
            using Microsoft.AspNetCore.Routing;

            IEndpointRouteBuilder app = new EndpointRouteBuilder();
            RouteGroupBuilder group = app.MapGroup("/api");

            group.MapPost("/payments", () =>
            {
                return new HttpClient();
            });

            namespace Microsoft.AspNetCore.Routing
            {
                public interface IEndpointRouteBuilder
                {
                    RouteGroupBuilder MapGroup(string prefix);
                }

                public sealed class EndpointRouteBuilder : IEndpointRouteBuilder
                {
                    public RouteGroupBuilder MapGroup(string prefix) => new();
                }

                public sealed class RouteGroupBuilder : IEndpointRouteBuilder
                {
                    public RouteGroupBuilder MapGroup(string prefix) => new();
                    public void MapPost(string pattern, Func<HttpClient> handler)
                    {
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR001_NewHttpClientInRequestPathAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR001, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenPlainTopLevelHttpClientHasNoRequestPathEvidence()
    {
        const string source = """
            using System.Net.Http;

            var client = new HttpClient();
            """;

        var diagnostics = await AnalyzerVerifier<HCR001_NewHttpClientInRequestPathAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenCustomMapGetReceiverCreatesHttpClient()
    {
        const string source = """
            using System;
            using System.Net.Http;

            var mapper = new CustomMapper();

            mapper.MapGet("client", () =>
            {
                return new HttpClient();
            });

            public sealed class CustomMapper
            {
                public void MapGet(string name, Func<HttpClient> factory)
                {
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR001_NewHttpClientInRequestPathAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenLookalikeAppReceiverCreatesHttpClient()
    {
        const string source = """
            using System;
            using System.Net.Http;

            var app = new CustomMapper();

            app.MapGet("client", () =>
            {
                return new HttpClient();
            });

            public sealed class CustomMapper
            {
                public void MapGet(string name, Func<HttpClient> factory)
                {
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR001_NewHttpClientInRequestPathAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenPlainLocalHttpClientHasNoRequestPathEvidence()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Utility
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
    public async Task DoesNotReport_WhenHttpClientIsCreatedInFactMethod()
    {
        const string source = """
            using System.Net.Http;

            public sealed class PaymentsService
            {
                [Fact]
                public HttpClient Create()
                {
                    return new HttpClient();
                }
            }

            public sealed class FactAttribute : System.Attribute
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR001_NewHttpClientInRequestPathAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenHttpClientIsCreatedInAttributedTestClass()
    {
        const string source = """
            using System.Net.Http;

            [TestClass]
            public sealed class PaymentsService
            {
                public HttpClient Create()
                {
                    return new HttpClient();
                }
            }

            public sealed class TestClassAttribute : System.Attribute
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR001_NewHttpClientInRequestPathAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenHttpClientIsCreatedInNUnitTestFixture()
    {
        const string source = """
            using System.Net.Http;

            [TestFixture]
            public sealed class PaymentsService
            {
                public HttpClient Create()
                {
                    return new HttpClient();
                }
            }

            public sealed class TestFixtureAttribute : System.Attribute
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR001_NewHttpClientInRequestPathAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenHttpClientIsCreatedInNUnitSetupMethod()
    {
        const string source = """
            using System.Net.Http;

            public sealed class PaymentsService
            {
                [SetUp]
                public void Create()
                {
                    _ = new HttpClient();
                }
            }

            public sealed class SetUpAttribute : System.Attribute
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR001_NewHttpClientInRequestPathAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenHttpClientIsCreatedInMSTestInitializeMethod()
    {
        const string source = """
            using System.Net.Http;

            public sealed class PaymentsService
            {
                [TestInitialize]
                public void Create()
                {
                    _ = new HttpClient();
                }
            }

            public sealed class TestInitializeAttribute : System.Attribute
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR001_NewHttpClientInRequestPathAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenHttpClientIsCreatedInMSTestCleanupMethod()
    {
        const string source = """
            using System.Net.Http;

            public sealed class PaymentsService
            {
                [TestCleanup]
                public void Create()
                {
                    _ = new HttpClient();
                }
            }

            public sealed class TestCleanupAttribute : System.Attribute
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR001_NewHttpClientInRequestPathAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResolvedTypeIsCustomHttpClient()
    {
        const string source = """
            namespace Custom
            {
                public sealed class HttpClient
                {
                }
            }

            public sealed class PaymentsService
            {
                public Custom.HttpClient Create()
                {
                    return new Custom.HttpClient();
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

    [Fact]
    public async Task CodeFix_UsesExistingPrimaryConstructorFactoryParameter()
    {
        const string source = """
            using System.Net.Http;

            public sealed class PaymentsService(IHttpClientFactory httpClientFactory)
            {
                public HttpClient Create()
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
