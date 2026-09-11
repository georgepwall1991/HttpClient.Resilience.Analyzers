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
}
