using HttpClient.Resilience.Analyzers.Analyzers.TypedClients;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.TypedClients;

public sealed class HCR084_StringlyNamedClientAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenNamedClientLiteralIsDuplicatedAtCreateClient()
    {
        const string source = """
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient("payments");
                }
            }

            public sealed class PaymentsService
            {
                public HttpClient Create(IHttpClientFactory factory)
                {
                    return factory.CreateClient("payments");
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR084_StringlyNamedClientAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR084, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenRegistrationUsesInlineConstantConcatenation()
    {
        const string source = """
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient("pay" + "ments");
                }
            }

            public sealed class PaymentsService
            {
                public HttpClient Create(IHttpClientFactory factory)
                {
                    return factory.CreateClient("payments");
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR084_StringlyNamedClientAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR084, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenCreateClientUsesInlineConstantConcatenation()
    {
        const string source = """
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient("payments");
                }
            }

            public sealed class PaymentsService
            {
                public HttpClient Create(IHttpClientFactory factory)
                {
                    return factory.CreateClient("pay" + "ments");
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR084_StringlyNamedClientAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR084, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenCreateClientConcatenationIsNotConstant()
    {
        const string source = """
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient("payments");
                }
            }

            public sealed class PaymentsService
            {
                public HttpClient Create(IHttpClientFactory factory, string suffix)
                {
                    return factory.CreateClient("pay" + suffix);
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR084_StringlyNamedClientAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenRegistrationUsesInlineConstantConditional()
    {
        const string source = """
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient(true ? "payments" : "search");
                }
            }

            public sealed class PaymentsService
            {
                public HttpClient Create(IHttpClientFactory factory)
                {
                    return factory.CreateClient("payments");
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR084_StringlyNamedClientAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR084, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenCreateClientUsesInlineConstantInterpolation()
    {
        const string source = """
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient("payments");
                }
            }

            public sealed class PaymentsService
            {
                public HttpClient Create(IHttpClientFactory factory)
                {
                    return factory.CreateClient($"pay{"ments"}");
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR084_StringlyNamedClientAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR084, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenCreateClientInterpolationIsNotConstant()
    {
        const string source = """
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient("payments");
                }
            }

            public sealed class PaymentsService
            {
                public HttpClient Create(IHttpClientFactory factory, string suffix)
                {
                    return factory.CreateClient($"pay{suffix}");
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR084_StringlyNamedClientAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenCreateClientUsesUnreassignedLiteralLocal()
    {
        const string source = """
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient("payments");
                }
            }

            public sealed class PaymentsService
            {
                public HttpClient Create(IHttpClientFactory factory)
                {
                    var clientName = "payments";
                    return factory.CreateClient(clientName);
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR084_StringlyNamedClientAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR084, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenCreateClientLiteralLocalIsReassigned()
    {
        const string source = """
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient("payments");
                }
            }

            public sealed class PaymentsService
            {
                public HttpClient Create(IHttpClientFactory factory)
                {
                    var clientName = "payments";
                    clientName = "search";
                    return factory.CreateClient(clientName);
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR084_StringlyNamedClientAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenAddHttpClientUsesUnreassignedLiteralLocal()
    {
        const string source = """
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    var clientName = "payments";
                    services.AddHttpClient(clientName);
                }
            }

            public sealed class PaymentsService
            {
                public HttpClient Create(IHttpClientFactory factory)
                {
                    return factory.CreateClient("payments");
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR084_StringlyNamedClientAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR084, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenAddHttpClientLiteralLocalIsReassigned()
    {
        const string source = """
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    var clientName = "payments";
                    clientName = "search";
                    services.AddHttpClient(clientName);
                }
            }

            public sealed class PaymentsService
            {
                public HttpClient Create(IHttpClientFactory factory)
                {
                    return factory.CreateClient("payments");
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR084_StringlyNamedClientAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenCreateClientUsesSplitAssignedLiteralLocal()
    {
        const string source = """
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient("payments");
                }
            }

            public sealed class PaymentsService
            {
                public HttpClient Create(IHttpClientFactory factory)
                {
                    string clientName;
                    clientName = "payments";
                    return factory.CreateClient(clientName);
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR084_StringlyNamedClientAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR084, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenAddHttpClientUsesSplitAssignedLiteralLocal()
    {
        const string source = """
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    string clientName;
                    clientName = "payments";
                    services.AddHttpClient(clientName);
                }
            }

            public sealed class PaymentsService
            {
                public HttpClient Create(IHttpClientFactory factory)
                {
                    return factory.CreateClient("payments");
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR084_StringlyNamedClientAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR084, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenSplitAssignedNameHasConditionalMutation()
    {
        const string source = """
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient("payments");
                }
            }

            public sealed class PaymentsService
            {
                public HttpClient Create(IHttpClientFactory factory, bool useSearch)
                {
                    string clientName;
                    clientName = "payments";
                    if (useSearch)
                    {
                        clientName = "search";
                    }

                    return factory.CreateClient(clientName);
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR084_StringlyNamedClientAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenNamedClientUsesSharedConstant()
    {
        const string source = """
            using System.Net.Http;

            public static class ClientNames
            {
                public const string Payments = "payments";
            }

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient(ClientNames.Payments);
                }
            }

            public sealed class PaymentsService
            {
                public HttpClient Create(IHttpClientFactory factory)
                {
                    return factory.CreateClient(ClientNames.Payments);
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR084_StringlyNamedClientAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenCreateClientUsesDifferentName()
    {
        const string source = """
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient("payments");
                }
            }

            public sealed class SearchService
            {
                public HttpClient Create(IHttpClientFactory factory)
                {
                    return factory.CreateClient("search");
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR084_StringlyNamedClientAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenFactoryIsCustomLookalike()
    {
        const string source = """
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient("payments");
                }
            }

            public sealed class PaymentsService
            {
                public string Create(CustomFactory factory)
                {
                    return factory.CreateClient("payments");
                }
            }

            public sealed class CustomFactory
            {
                public string CreateClient(string name)
                {
                    return name;
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name)
                {
                    return default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR084_StringlyNamedClientAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenRegistrationReceiverIsCustomLookalike()
    {
        const string source = """
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(CustomServices services)
                {
                    services.AddHttpClient("payments");
                }
            }

            public sealed class PaymentsService
            {
                public HttpClient Create(IHttpClientFactory factory)
                {
                    return factory.CreateClient("payments");
                }
            }

            public sealed class CustomServices
            {
                public void AddHttpClient(string name)
                {
                }
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR084_StringlyNamedClientAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResolvedRegistrationTypeHasCustomNamespace()
    {
        const string source = """
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(Custom.IServiceCollection services)
                {
                    services.AddHttpClient("payments");
                }
            }

            public sealed class PaymentsService
            {
                public HttpClient Create(IHttpClientFactory factory)
                {
                    return factory.CreateClient("payments");
                }
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            namespace Custom
            {
                public interface IServiceCollection
                {
                    void AddHttpClient(string name);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR084_StringlyNamedClientAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResolvedFactoryTypeHasCustomNamespace()
    {
        const string source = """
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient("payments");
                }
            }

            public sealed class PaymentsService
            {
                public HttpClient Create(Custom.IHttpClientFactory factory)
                {
                    return factory.CreateClient("payments");
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name)
                {
                    return default!;
                }
            }

            namespace Custom
            {
                public interface IHttpClientFactory
                {
                    HttpClient CreateClient(string name);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR084_StringlyNamedClientAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenCustomCreateClientExtensionUsesRegisteredLiteral()
    {
        const string source = """
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient("payments");
                }
            }

            public sealed class PaymentsService
            {
                public HttpClient Create(IHttpClientFactory factory)
                {
                    return factory.CreateClient("payments", useCustomClient: true);
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name)
                {
                    return default!;
                }
            }

            public static class CustomFactoryExtensions
            {
                public static HttpClient CreateClient(
                    this IHttpClientFactory factory,
                    string name,
                    bool useCustomClient)
                {
                    return new HttpClient();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR084_StringlyNamedClientAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenAddHttpClientLookalikeDoesNotReturnBuilder()
    {
        const string source = """
            using System.Net.Http;

            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient("payments");
                }
            }

            public sealed class PaymentsService
            {
                public HttpClient Create(IHttpClientFactory factory)
                {
                    return factory.CreateClient("payments");
                }
            }

            public interface IServiceCollection
            {
                void AddHttpClient(string name);
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR084_StringlyNamedClientAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }
}
