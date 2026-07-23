using HttpClient.Resilience.Analyzers.Analyzers.Lifetime;
using HttpClient.Resilience.Analyzers.CodeFixes;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.Lifetime;

public sealed class HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenStaticHttpClientHasDefaultHandler()
    {
        const string source = """
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static readonly HttpClient Client = new();
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR002, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenStaticHttpClientHasNullForgivingDefaultHandler()
    {
        const string source = """
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static readonly HttpClient Client = new HttpClient()!;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR002, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenStaticHttpClientIsAssignedDefaultHandler()
    {
        const string source = """
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static HttpClient Client = null!;

                public static void Initialize()
                {
                    Client = new HttpClient();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR002, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenStaticHttpClientIsAssignedLocalDefaultClient()
    {
        const string source = """
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static HttpClient Client = null!;

                public static void Initialize()
                {
                    var client = new HttpClient();
                    Client = client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR002, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenStaticHttpClientIsAssignedNullForgivingLocalDefaultClient()
    {
        const string source = """
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static HttpClient Client = null!;

                public static void Initialize()
                {
                    var client = new HttpClient()!;
                    Client = client!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR002, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenStaticHttpClientUsesReassignedConfiguredLocalHandler()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static HttpClient Client = null!;

                public static void Initialize()
                {
                    var handler = new SocketsHttpHandler
                    {
                        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                    };
                    handler = new SocketsHttpHandler();

                    Client = new HttpClient(handler);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR002, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenStaticHttpClientUsesReassignedHandlerAfterPooledConnectionLifetimeAssignment()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static HttpClient Client = null!;

                public static void Initialize()
                {
                    var handler = new SocketsHttpHandler();
                    handler.PooledConnectionLifetime = TimeSpan.FromMinutes(2);
                    handler = new SocketsHttpHandler();

                    Client = new HttpClient(handler);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR002, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenStaticHttpClientPropertyHasDefaultHandler()
    {
        const string source = """
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static HttpClient Client { get; } = new();
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR002, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenStaticHttpClientPropertyIsAssignedDefaultHandler()
    {
        const string source = """
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static HttpClient Client { get; set; } = null!;

                public static void Initialize()
                {
                    Client = new HttpClient();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR002, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenStaticHttpClientIsAssignedLocalConfiguredClient()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static HttpClient Client = null!;

                public static void Initialize()
                {
                    var client = new HttpClient(new SocketsHttpHandler
                    {
                        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                    });
                    Client = client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenLocalClientIsReassignedBeforeLongLivedAssignment()
    {
        const string source = """
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static HttpClient Client = null!;

                public static void Initialize(HttpClient replacement)
                {
                    var client = new HttpClient();
                    client = replacement;
                    Client = client;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenStaticHttpClientHasPooledConnectionLifetime()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static readonly HttpClient Client = new(
                    new SocketsHttpHandler
                    {
                        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                    });
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenStaticHttpClientUsesConfiguredLocalHandler()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static HttpClient Client = null!;

                public static void Initialize()
                {
                    var handler = new SocketsHttpHandler
                    {
                        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                    };

                    Client = new HttpClient(handler);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenStaticHttpClientUsesNullForgivingConfiguredLocalHandler()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static HttpClient Client = null!;

                public static void Initialize()
                {
                    var handler = new SocketsHttpHandler
                    {
                        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                    }!;

                    Client = new HttpClient(handler!);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenStaticHttpClientUsesLocalHandlerConfiguredByAssignment()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static HttpClient Client = null!;

                public static void Initialize()
                {
                    var handler = new SocketsHttpHandler();
                    handler.PooledConnectionLifetime = TimeSpan.FromMinutes(2);

                    Client = new HttpClient(handler);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenStaticHttpClientPropertyHasPooledConnectionLifetime()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static HttpClient Client { get; } = new(
                    new SocketsHttpHandler
                    {
                        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                    });
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenStaticHttpClientUsesConfiguredHandlerField()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static readonly SocketsHttpHandler Handler = new()
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                };

                private static readonly HttpClient Client = new(Handler);
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenStaticHttpClientUsesNullForgivingConfiguredHandlerField()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static readonly SocketsHttpHandler Handler = new()
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                }!;

                private static readonly HttpClient Client = new(Handler!);
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenStaticHttpClientUsesConfiguredHandlerProperty()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static SocketsHttpHandler Handler { get; } = new()
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                };

                private static readonly HttpClient Client = new(Handler);
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenHttpClientFieldIsNotStatic()
    {
        const string source = """
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private readonly HttpClient _client = new();
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenHttpClientPropertyIsNotStatic()
    {
        const string source = """
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private HttpClient Client { get; } = new();
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenStaticFieldUsesResolvedCustomHttpClient()
    {
        const string source = """
            namespace Custom
            {
                public sealed class HttpClient
                {
                }
            }

            public sealed class GitHubClient
            {
                private static readonly Custom.HttpClient Client = new Custom.HttpClient();
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenRegisteredSingletonOwnsInstanceHttpClientField()
    {
        const string source = """
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<GitHubClient>();
                }
            }

            public sealed class GitHubClient
            {
                private readonly HttpClient _client = new();
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR002, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenSingletonRegistrationUsesServiceCollectionAlias()
    {
        const string source = """
            using System.Net.Http;
            using Services = global::IServiceCollection;

            public static class Registrations
            {
                public static void Configure(Services services)
                {
                    services.AddSingleton<GitHubClient>();
                }
            }

            public sealed class GitHubClient
            {
                private readonly HttpClient _client = new();
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR002, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenRegisteredSingletonOwnsInstanceHttpClientProperty()
    {
        const string source = """
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<GitHubClient>();
                }
            }

            public sealed class GitHubClient
            {
                private HttpClient Client { get; } = new();
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR002, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenRegisteredSingletonAssignsInstanceHttpClientField()
    {
        const string source = """
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<GitHubClient>();
                }
            }

            public sealed class GitHubClient
            {
                private readonly HttpClient _client;

                public GitHubClient()
                {
                    _client = new HttpClient();
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

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR002, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenRegisteredSingletonAssignsInstanceHttpClientProperty()
    {
        const string source = """
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<GitHubClient>();
                }
            }

            public sealed class GitHubClient
            {
                private HttpClient Client { get; set; } = null!;

                public GitHubClient()
                {
                    Client = new HttpClient();
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

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR002, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenSingletonFactoryConstructsImplementationOwningHttpClientField()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IGitHubClient>(sp => new GitHubClient());
                }
            }

            public interface IGitHubClient
            {
            }

            public sealed class GitHubClient : IGitHubClient
            {
                private readonly HttpClient _client = new();
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR002, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTypeofSingletonFactoryConstructsImplementationOwningHttpClientField()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton(typeof(IGitHubClient), sp => new GitHubClient());
                }
            }

            public interface IGitHubClient
            {
            }

            public sealed class GitHubClient : IGitHubClient
            {
                private readonly HttpClient _client = new();
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddSingleton(this IServiceCollection services, Type serviceType, Func<IServiceProvider, object> factory) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR002, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenQualifiedSingletonOwnsInstanceHttpClientField()
    {
        const string source = """
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<Clients.GitHubClient>();
                }
            }

            namespace Clients
            {
                public sealed class GitHubClient
                {
                    private readonly HttpClient _client = new();
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

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR002, diagnostic.Id);
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
                    services.AddSingleton<Jobs.GitHubClient>();
                }
            }

            namespace Other
            {
                public sealed class GitHubClient
                {
                    private readonly HttpClient _client = new();
                }
            }

            namespace Jobs
            {
                public sealed class GitHubClient
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
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenRegisteredSingletonOwnsConfiguredInstanceHttpClientField()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<GitHubClient>();
                }
            }

            public sealed class GitHubClient
            {
                private readonly HttpClient _client = new(
                    new SocketsHttpHandler
                    {
                        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                    });
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenRegisteredSingletonUsesConfiguredHandlerField()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<GitHubClient>();
                }
            }

            public sealed class GitHubClient
            {
                private static readonly SocketsHttpHandler Handler = new()
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                };

                private readonly HttpClient _client = new(Handler);
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenRegisteredSingletonUsesConfiguredHandlerProperty()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<GitHubClient>();
                }
            }

            public sealed class GitHubClient
            {
                private static SocketsHttpHandler Handler { get; } = new()
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                };

                private readonly HttpClient _client = new(Handler);
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenRegisteredSingletonAssignsConfiguredInstanceHttpClientField()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<GitHubClient>();
                }
            }

            public sealed class GitHubClient
            {
                private readonly HttpClient _client;

                public GitHubClient()
                {
                    _client = new HttpClient(new SocketsHttpHandler
                    {
                        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                    });
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

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
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
                    builder.AddSingleton<GitHubClient>();
                }
            }

            public sealed class GitHubClient
            {
                private readonly HttpClient _client = new();
            }

            public sealed class CustomBuilder
            {
                public CustomBuilder AddSingleton<TService>() => this;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenSingletonRegistrationMethodIsOwnedByCustomNamespace()
    {
        const string source = """
            using System.Net.Http;
            using Custom.DependencyInjection;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<GitHubClient>();
                }
            }

            public sealed class GitHubClient
            {
                private readonly HttpClient _client = new();
            }

            public interface IServiceCollection
            {
            }

            namespace Custom.DependencyInjection
            {
                public static class ServiceCollectionExtensions
                {
                    public static global::IServiceCollection AddSingleton<TService>(
                        this global::IServiceCollection services) => services;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task CodeFix_AddsSocketsHttpHandlerWithPooledConnectionLifetime()
    {
        const string source = """
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static readonly HttpClient Client = new();
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer, HCR002_AddPooledConnectionLifetimeCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains("new HttpClient(new SocketsHttpHandler", fixedSource);
        Assert.Contains("PooledConnectionLifetime = System.TimeSpan.FromMinutes(2)", fixedSource);
    }

    [Fact]
    public async Task CodeFix_IsNotOffered_WhenHttpClientAlreadyUsesHandlerArgument()
    {
        const string source = """
            using System.Net.Http;

            public sealed class GitHubClient
            {
                private static readonly HttpClient Client = new(new SocketsHttpHandler());
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer>.GetDiagnosticsAsync(source);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR002, diagnostic.Id);

        var titles = await CodeFixVerifier<HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer, HCR002_AddPooledConnectionLifetimeCodeFixProvider>
            .GetCodeFixTitlesAsync(source);

        Assert.Empty(titles);
    }
}
