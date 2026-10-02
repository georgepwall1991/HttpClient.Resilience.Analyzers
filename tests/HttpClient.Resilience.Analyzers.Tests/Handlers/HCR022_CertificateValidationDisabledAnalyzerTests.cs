using HttpClient.Resilience.Analyzers.Analyzers.Handlers;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.Handlers;

public sealed class HCR022_CertificateValidationDisabledAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenCallbackAlwaysReturnsTrue()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClientHandler Create()
                {
                    var handler = new HttpClientHandler();
                    handler.ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) => true;
                    return handler;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR022_CertificateValidationDisabledAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR022, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenObjectInitializerCallbackAlwaysReturnsTrue()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClientHandler Create()
                {
                    return new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) => true
                    };
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR022_CertificateValidationDisabledAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR022, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenBlockBodiedLambdaAlwaysReturnsTrue()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClientHandler Create()
                {
                    var handler = new HttpClientHandler();
                    handler.ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) =>
                    {
                        return true;
                    };
                    return handler;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR022_CertificateValidationDisabledAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR022, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenDangerousValidatorIsAssigned()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClientHandler Create()
                {
                    var handler = new HttpClientHandler();
                    handler.ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                    return handler;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR022_CertificateValidationDisabledAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR022, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenCallbackIsAssignedInsideConfigurePrimaryHandler()
    {
        const string source = """
            using System.Net.Http;

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient("payments")
                        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                        {
                            ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) => true
                        });
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name) => null!;

                public static IHttpClientBuilder ConfigurePrimaryHttpMessageHandler(
                    this IHttpClientBuilder builder,
                    System.Func<HttpMessageHandler> configure) => builder;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR022_CertificateValidationDisabledAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR022, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenCallbackInspectsCertificate()
    {
        const string source = """
            using System.Net.Http;
            using System.Security.Cryptography.X509Certificates;

            public sealed class Factory
            {
                public HttpClientHandler Create()
                {
                    var handler = new HttpClientHandler();
                    handler.ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) =>
                        certificate is X509Certificate2 x509 && x509.Thumbprint == "ABC";
                    return handler;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR022_CertificateValidationDisabledAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenCallbackReturnsConditionally()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Factory
            {
                public HttpClientHandler Create()
                {
                    var handler = new HttpClientHandler();
                    handler.ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) =>
                        errors == System.Net.Security.SslPolicyErrors.None;
                    return handler;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR022_CertificateValidationDisabledAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenCustomHandlerLookalikeAssignsCallback()
    {
        const string source = """
            public sealed class CustomHttpClientHandler
            {
                public System.Func<object, object, object, object, bool>? ServerCertificateCustomValidationCallback { get; set; }
            }

            public sealed class Factory
            {
                public CustomHttpClientHandler Create()
                {
                    var handler = new CustomHttpClientHandler();
                    handler.ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) => true;
                    return handler;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR022_CertificateValidationDisabledAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenCallbackIsAssignedInTestClass()
    {
        const string source = """
            using System.Net.Http;

            public sealed class HttpTests
            {
                public HttpClientHandler Create()
                {
                    var handler = new HttpClientHandler();
                    handler.ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) => true;
                    return handler;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR022_CertificateValidationDisabledAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }
}
