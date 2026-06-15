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
    public async Task ReportsDiagnostic_WhenFactoryClientLocalIsAssignedToStaticField()
    {
        const string source = """
            using System.Net.Http;

            public sealed class ClientCache
            {
                private static HttpClient _client = null!;

                public static void Initialize(IHttpClientFactory factory)
                {
                    var client = factory.CreateClient("github");
                    _client = client;
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
    public async Task ReportsDiagnostic_WhenFactoryClientIsInitializedIntoStaticProperty()
    {
        const string source = """
            using System.Net.Http;

            public sealed class ClientCache
            {
                private static readonly IHttpClientFactory Factory = null!;
                private static HttpClient Client { get; } = Factory.CreateClient("github");
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
    public async Task DoesNotReport_WhenLookalikeFactoryValueIsAssignedToStaticNonHttpClientField()
    {
        const string source = """
            public sealed class ClientCache
            {
                private static string _client = "";

                public static void Initialize(IHttpClientFactory factory)
                {
                    _client = factory.CreateClient("github");
                }
            }

            public interface IHttpClientFactory
            {
                string CreateClient(string name);
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR003_CachedFactoryClientAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenLookalikeFactoryValueInitializesStaticNonHttpClientProperty()
    {
        const string source = """
            public sealed class ClientCache
            {
                private static readonly IHttpClientFactory Factory = null!;

                private static string Client { get; } = Factory.CreateClient("github");
            }

            public interface IHttpClientFactory
            {
                string CreateClient(string name);
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
    public async Task ReportsDiagnostic_WhenFactoryClientLocalIsAssignedToFieldOnRegisteredSingleton()
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
                    var client = factory.CreateClient("github");
                    _client = client;
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
    public async Task DoesNotReport_WhenFactoryClientLocalIsReassignedBeforeLongLivedAssignment()
    {
        const string source = """
            using System.Net.Http;

            public sealed class ClientCache
            {
                private static HttpClient _client = null!;

                public static void Initialize(IHttpClientFactory factory, HttpClient replacement)
                {
                    var client = factory.CreateClient("github");
                    client = replacement;
                    _client = client;
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
    public async Task ReportsDiagnostic_WhenFactoryClientIsAssignedToPropertyOnRegisteredSingleton()
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
                private HttpClient Client { get; set; } = null!;

                public ClientRunner(IHttpClientFactory factory)
                {
                    Client = factory.CreateClient("github");
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
    public async Task DoesNotReport_WhenFactoryClientIsAssignedToPropertyOnTransientService()
    {
        const string source = """
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddTransient<ClientRunner>();
                }
            }

            public sealed class ClientRunner
            {
                private HttpClient Client { get; set; } = null!;

                public ClientRunner(IHttpClientFactory factory)
                {
                    Client = factory.CreateClient("github");
                }
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddTransient<TService>(this IServiceCollection services) => services;
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
    public async Task DoesNotReport_WhenUnresolvedProviderMemberSharesFactoryName()
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
                private static readonly IHttpClientFactory Factory = null!;

                private readonly HttpClient _client = UnknownProvider.Factory.CreateClient("github");
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
    public async Task ReportsDiagnostic_WhenSingletonFactoryConstructsImplementationThatCachesFactoryClient()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IClientRunner>(sp => new ClientRunner(sp.GetRequiredService<IHttpClientFactory>()));
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
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory) => services;
            }

            public static class ServiceProviderExtensions
            {
                public static TService GetRequiredService<TService>(this IServiceProvider serviceProvider) => throw new NotImplementedException();
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
    public async Task ReportsDiagnostic_WhenTypeofSingletonFactoryConstructsImplementationThatCachesFactoryClient()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IClientRunner), sp => new ClientRunner(sp.GetRequiredService<IHttpClientFactory>()));
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
                public static IServiceCollection AddSingleton(this IServiceCollection services, Type serviceType, Func<IServiceProvider, object> factory) => services;
            }

            public static class ServiceProviderExtensions
            {
                public static TService GetRequiredService<TService>(this IServiceProvider serviceProvider) => throw new NotImplementedException();
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
