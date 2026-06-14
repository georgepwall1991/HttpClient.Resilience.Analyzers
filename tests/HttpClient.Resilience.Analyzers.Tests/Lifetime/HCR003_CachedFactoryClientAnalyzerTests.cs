using HttpClient.Resilience.Analyzers.Analyzers.Lifetime;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.Lifetime;

public sealed class HCR003_CachedFactoryClientAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenFactoryClientIsAssignedToStaticField()
    {
        const string source = """
            using System.Net.Http;

            public sealed class ClientCache
            {
                private static HttpClient _client = null!;

                public static void Initialize(IHttpClientFactory factory)
                {
                    _client = factory.CreateClient("github");
                }
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR003_CachedFactoryClientAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR003, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenFactoryClientIsInitializedIntoStaticField()
    {
        const string source = """
            using System.Net.Http;

            public sealed class ClientCache
            {
                private static readonly IHttpClientFactory Factory = null!;
                private static readonly HttpClient Client = Factory.CreateClient("github");
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR003_CachedFactoryClientAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR003, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenFactoryClientIsUsedAsLocal()
    {
        const string source = """
            using System.Net.Http;

            public sealed class ClientRunner
            {
                public void Run(IHttpClientFactory factory)
                {
                    var client = factory.CreateClient("github");
                }
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR003_CachedFactoryClientAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenLookalikeFactoryClientIsAssignedToStaticField()
    {
        const string source = """
            using System.Net.Http;

            public sealed class ClientCache
            {
                private static HttpClient _client = null!;

                public static void Initialize(CustomFactory factory)
                {
                    _client = factory.CreateClient("github");
                }
            }

            public sealed class CustomFactory
            {
                public HttpClient CreateClient(string name) => new();
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR003_CachedFactoryClientAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenQualifiedCustomFactoryClientIsAssignedToStaticField()
    {
        const string source = """
            using System.Net.Http;

            public sealed class ClientCache
            {
                private static HttpClient _client = null!;

                public static void Initialize(Custom.IHttpClientFactory factory)
                {
                    _client = factory.CreateClient("github");
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

        var diagnostics = await AnalyzerVerifier<HCR003_CachedFactoryClientAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenLookalikeFactoryClientIsInitializedIntoStaticField()
    {
        const string source = """
            using System.Net.Http;

            public sealed class ClientCache
            {
                private static readonly CustomFactory Factory = new();
                private static readonly HttpClient Client = Factory.CreateClient("github");
            }

            public sealed class CustomFactory
            {
                public HttpClient CreateClient(string name) => new();
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR003_CachedFactoryClientAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenFactoryClientIsAssignedToFieldOnRegisteredSingleton()
    {
        const string source = """
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<ClientRunner>();
                }
            }

            public sealed class ClientRunner
            {
                private HttpClient _client = null!;

                public ClientRunner(IHttpClientFactory factory)
                {
                    _client = factory.CreateClient("github");
                }
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR003_CachedFactoryClientAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR003, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenFactoryClientIsAssignedToFieldOnQualifiedSingleton()
    {
        const string source = """
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<Clients.ClientRunner>();
                }
            }

            namespace Clients
            {
                public sealed class ClientRunner
                {
                    private HttpClient _client = null!;

                    public ClientRunner(IHttpClientFactory factory)
                    {
                        _client = factory.CreateClient("github");
                    }
                }
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR003_CachedFactoryClientAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR003, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenQualifiedSingletonRegistrationTargetsDifferentSameNamedType()
    {
        const string source = """
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<Jobs.ClientRunner>();
                }
            }

            namespace Other
            {
                public sealed class ClientRunner
                {
                    private HttpClient _client = null!;

                    public ClientRunner(IHttpClientFactory factory)
                    {
                        _client = factory.CreateClient("github");
                    }
                }
            }

            namespace Jobs
            {
                public sealed class ClientRunner
                {
                }
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR003_CachedFactoryClientAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenFactoryClientIsInitializedIntoRegisteredSingletonField()
    {
        const string source = """
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<ClientRunner>();
                }
            }

            public sealed class ClientRunner
            {
                private readonly HttpClient _client = FactoryProvider.Factory.CreateClient("github");
            }

            public static class FactoryProvider
            {
                public static IHttpClientFactory Factory { get; } = null!;
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR003_CachedFactoryClientAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR003, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenFactoryClientIsAssignedToFieldOnRegisteredSingletonImplementation()
    {
        const string source = """
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IClientRunner, ClientRunner>();
                }
            }

            public interface IClientRunner
            {
            }

            public sealed class ClientRunner : IClientRunner
            {
                private HttpClient _client = null!;

                public ClientRunner(IHttpClientFactory factory)
                {
                    _client = factory.CreateClient("github");
                }
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddSingleton<TService, TImplementation>(this IServiceCollection services) => services;
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR003_CachedFactoryClientAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR003, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenSingletonRegistrationAndFactoryCacheAreInDifferentFiles()
    {
        const string registrations = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<ClientRunner>();
                }
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;
            }
            """;

        const string service = """
            using System.Net.Http;

            public sealed class ClientRunner
            {
                private HttpClient _client = null!;

                public ClientRunner(IHttpClientFactory factory)
                {
                    _client = factory.CreateClient("github");
                }
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR003_CachedFactoryClientAnalyzer>.GetDiagnosticsAsync(registrations, service);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR003, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenLookalikeSingletonRegistrationIsNotIServiceCollection()
    {
        const string source = """
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(CustomBuilder builder)
                {
                    builder.AddSingleton<ClientRunner>();
                }
            }

            public sealed class ClientRunner
            {
                private HttpClient _client = null!;

                public ClientRunner(IHttpClientFactory factory)
                {
                    _client = factory.CreateClient("github");
                }
            }

            public sealed class CustomBuilder
            {
                public CustomBuilder AddSingleton<TService>() => this;
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR003_CachedFactoryClientAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }
}
