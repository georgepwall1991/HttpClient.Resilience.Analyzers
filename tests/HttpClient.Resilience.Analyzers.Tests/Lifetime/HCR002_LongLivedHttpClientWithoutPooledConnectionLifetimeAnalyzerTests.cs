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
