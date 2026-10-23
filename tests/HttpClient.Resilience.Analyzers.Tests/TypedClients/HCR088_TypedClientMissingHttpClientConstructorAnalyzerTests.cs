using HttpClient.Resilience.Analyzers.Analyzers.TypedClients;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.TypedClients;

public sealed class HCR088_TypedClientMissingHttpClientConstructorAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenTypedClientHasNoHttpClientConstructor()
    {
        var source = "using System.Net.Http;\n" + CustomPipelineSources.FrameworkStubs + """

            public sealed class GitHubClient
            {
                public GitHubClient(string apiKey) { }
            }

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<GitHubClient>();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR088, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenImplementationTypeHasNoHttpClientConstructor()
    {
        var source = "using System.Net.Http;\n" + CustomPipelineSources.FrameworkStubs + """

            public static class TwoTypeArgumentExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TService, TImplementation>(
                    this IServiceCollection services,
                    System.Func<HttpClient, TImplementation> factory) => null!;
            }

            public interface IGitHubClient { }

            public sealed class GitHubClient : IGitHubClient
            {
                public GitHubClient() { }
            }

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<IGitHubClient, GitHubClient>();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR088, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenTypedClientAcceptsHttpClient()
    {
        var source = "using System.Net.Http;\n" + CustomPipelineSources.FrameworkStubs + """

            public sealed class GitHubClient
            {
                public GitHubClient(System.Net.Http.HttpClient httpClient) { }
            }

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<GitHubClient>();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenFactoryLambdaSuppliesTheInstance()
    {
        var source = "using System.Net.Http;\n" + CustomPipelineSources.FrameworkStubs + """

            public sealed class GitHubClient
            {
                public GitHubClient(string apiKey) { }
            }

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<GitHubClient>(client => new GitHubClient("key"));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenTypedClientIsAbstract()
    {
        var source = "using System.Net.Http;\n" + CustomPipelineSources.FrameworkStubs + """

            public abstract class GitHubClient
            {
                protected GitHubClient(string apiKey) { }
            }

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<GitHubClient>();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenAddHttpClientIsCustomApi()
    {
        const string source = """
            public sealed class GitHubClient
            {
                public GitHubClient(string apiKey) { }
            }

            namespace Custom.Http
            {
                public static class CustomExtensions
                {
                    public static void AddHttpClient<T>(this object services) { }
                }
            }

            public sealed class Startup
            {
                public void Configure(object services)
                {
                    Custom.Http.CustomExtensions.AddHttpClient<GitHubClient>(services);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }
    [Fact]
    public async Task ReportsDiagnostic_WhenConfigureLambdaDoesNotSupplyTheInstance()
    {
        var source = "using System.Net.Http;\n" + CustomPipelineSources.FrameworkStubs + """

            public sealed class GitHubClient
            {
                public GitHubClient(string apiKey) { }
            }

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<GitHubClient>(client =>
                    {
                        client.BaseAddress = new System.Uri("https://api.github.com/");
                    });
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR088, diagnostic.Id);
    }


    [Fact]
    public async Task DoesNotReport_WhenClassHasAnyHttpClientConstructor()
    {
        var source = "using System.Net.Http;\n" + CustomPipelineSources.FrameworkStubs + """

            public sealed class GitHubClient
            {
                public GitHubClient(string apiKey) { }

                public GitHubClient(System.Net.Http.HttpClient httpClient, string apiKey) { }
            }

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<GitHubClient>();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenClassHasOnlyImplicitConstructor()
    {
        var source = "using System.Net.Http;\n" + CustomPipelineSources.FrameworkStubs + """

            public sealed class GitHubClient
            {
            }

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<GitHubClient>();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR088, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenImplementationIsInterface()
    {
        var source = "using System.Net.Http;\n" + CustomPipelineSources.FrameworkStubs + """

            public interface IGitHubClient
            {
            }

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<IGitHubClient>();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenImplementationIsStruct()
    {
        var source = "using System.Net.Http;\n" + CustomPipelineSources.FrameworkStubs + """

            public struct GitHubClient
            {
            }

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<GitHubClient>();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenFactoryIsNamedArgument()
    {
        var source = "using System.Net.Http;\n" + CustomPipelineSources.FrameworkStubs + """

            public sealed class GitHubClient
            {
                public GitHubClient(string apiKey) { }
            }

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<GitHubClient>(
                        factory: client => new GitHubClient("key"));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenSecondTypeArgumentFactorySuppliesTheInstance()
    {
        var source = "using System.Net.Http;\n" + CustomPipelineSources.FrameworkStubs + """

            public static class TwoTypeArgumentExtensions
            {
                public static IHttpClientBuilder AddHttpClient<TService, TImplementation>(
                    this IServiceCollection services,
                    System.Func<HttpClient, TImplementation> factory) => null!;
            }

            public interface IGitHubClient { }

            public sealed class GitHubClient : IGitHubClient
            {
                public GitHubClient(string apiKey) { }
            }

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<IGitHubClient, GitHubClient>(
                        client => new GitHubClient("key"));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenAnonymousMethodSuppliesTheInstance()
    {
        var source = "using System.Net.Http;\n" + CustomPipelineSources.FrameworkStubs + """

            public sealed class GitHubClient
            {
                public GitHubClient(string apiKey) { }
            }

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<GitHubClient>(
                        delegate(System.Net.Http.HttpClient client)
                        {
                            return new GitHubClient("key");
                        });
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnresolvedFactoryLambdaSuppliesTheInstance()
    {
        const string source = """
            public sealed class GitHubClient
            {
                public GitHubClient(string apiKey) { }
            }

            public sealed class Startup
            {
                public void Configure(object services)
                {
                    services.AddHttpClient<GitHubClient>(client => new GitHubClient("key"));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnresolvedFactoryLambdaIsNotTheOnlyArgument()
    {
        const string source = """
            public sealed class GitHubClient
            {
                public GitHubClient(string apiKey) { }
            }

            public sealed class Startup
            {
                public void Configure(object services)
                {
                    services.AddHttpClient<GitHubClient>(client => new GitHubClient("key"), 0);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenUnresolvedInvocationHasNoFactoryLambda()
    {
        const string source = """
            public sealed class GitHubClient
            {
                public GitHubClient(string apiKey) { }
            }

            public sealed class Startup
            {
                public void Configure(object services)
                {
                    services.AddHttpClient<GitHubClient>(0);
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR088, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnresolvedServiceCollectionReceiverIsUsed()
    {
        const string source = """
            public sealed class GitHubClient
            {
                public GitHubClient(string apiKey) { }
            }

            public sealed class Startup
            {
                public void Configure(object serviceCollection)
                {
                    serviceCollection.AddHttpClient<GitHubClient>();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR088, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnresolvedReceiverIsNotServiceCollectionName()
    {
        const string source = """
            public sealed class GitHubClient
            {
                public GitHubClient(string apiKey) { }
            }

            public sealed class Startup
            {
                public void Configure(object container)
                {
                    container.AddHttpClient<GitHubClient>();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnresolvedReceiverIsNotAnIdentifier()
    {
        const string source = """
            public sealed class GitHubClient
            {
                public GitHubClient(string apiKey) { }
            }

            public sealed class Startup
            {
                public void Configure()
                {
                    GetServices().AddHttpClient<GitHubClient>();
                }

                private object GetServices() => new object();
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>
            .GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenAmbiguousCandidatesIncludeFactoryAndConfigure()
    {
        var source = "using System.Net.Http;\n" + CustomPipelineSources.FrameworkStubs + """

            public static class AmbiguousServiceCollectionExtensions
            {
                public static void AddHttpClient<TClient>(
                    this IServiceCollection services,
                    string name,
                    System.Func<HttpClient, TClient> factory) { }

                public static void AddHttpClient<TClient>(
                    this IServiceCollection services,
                    string name,
                    System.Action<HttpClient> configure) { }
            }

            public sealed class GitHubClient
            {
                public GitHubClient(string apiKey) { }
            }

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<GitHubClient>("name", client => new GitHubClient("key"));
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenAmbiguousCandidatesHaveNoFactoryArgument()
    {
        var source = "using System.Net.Http;\n" + CustomPipelineSources.FrameworkStubs + """

            public static class AmbiguousServiceCollectionExtensions
            {
                public static void AddHttpClient<TClient>(
                    this IServiceCollection services,
                    string name,
                    System.Action<HttpClient> first) { }

                public static void AddHttpClient<TClient>(
                    this IServiceCollection services,
                    string name,
                    System.Action<object> second) { }
            }

            public sealed class GitHubClient
            {
                public GitHubClient(string apiKey) { }
            }

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<GitHubClient>("name", client => { });
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR088, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenAmbiguousFrameworkAndCustomExtensionsArePresent()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using Custom;

            public interface IServiceCollection
            {
            }

            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class FrameworkExtensions
                {
                    public static void AddHttpClient<TClient>(this IServiceCollection services) { }
                }
            }

            namespace Custom
            {
                public static class CustomServiceCollectionExtensions
                {
                    public static void AddHttpClient<TClient>(this IServiceCollection services) { }
                }
            }

            public sealed class GitHubClient
            {
                public GitHubClient(string apiKey) { }
            }

            public sealed class Startup
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<GitHubClient>();
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR088_TypedClientMissingHttpClientConstructorAnalyzer>.GetDiagnosticsAllowingCompilerErrorsAsync(source);

        Assert.Empty(diagnostics);
    }
}
