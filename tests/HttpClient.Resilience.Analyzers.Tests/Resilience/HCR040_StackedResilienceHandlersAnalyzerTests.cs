using HttpClient.Resilience.Analyzers.Analyzers.Resilience;
using HttpClient.Resilience.Analyzers.CodeFixes;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.Resilience;

public sealed class HCR040_StackedResilienceHandlersAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenStandardResilienceHandlerIsStacked()
    {
        const string source = """
            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<GitHubClient>()
                        .AddStandardResilienceHandler()
                        .AddStandardResilienceHandler();
                }
            }

            public sealed class GitHubClient
            {
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient<T>(this IServiceCollection services)
                {
                    return null!;
                }

                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder)
                {
                    return builder;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR040_StackedResilienceHandlersAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR040, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenStandardResilienceHandlerIsUsedOnce()
    {
        const string source = """
            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<GitHubClient>()
                        .AddStandardResilienceHandler();
                }
            }

            public sealed class GitHubClient
            {
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient<T>(this IServiceCollection services)
                {
                    return null!;
                }

                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder)
                {
                    return builder;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR040_StackedResilienceHandlersAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenStandardResilienceHandlerIsStackedOnBuilderParameter()
    {
        const string source = """
            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IHttpClientBuilder builder)
                {
                    return builder
                        .AddStandardResilienceHandler()
                        .AddStandardResilienceHandler();
                }
            }

            public interface IHttpClientBuilder
            {
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder)
                {
                    return builder;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR040_StackedResilienceHandlersAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR040, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenLookalikeStandardResilienceHandlerIsStackedOnCustomBuilder()
    {
        const string source = """
            public static class Registrations
            {
                public static CustomBuilder Configure(CustomBuilder builder)
                {
                    return builder
                        .AddStandardResilienceHandler()
                        .AddStandardResilienceHandler();
                }
            }

            public sealed class CustomBuilder
            {
            }

            public static class CustomBuilderExtensions
            {
                public static CustomBuilder AddStandardResilienceHandler(this CustomBuilder builder)
                {
                    return builder;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR040_StackedResilienceHandlersAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenSameNamedCustomResilienceHandlerIsStacked()
    {
        const string source = """
            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<GitHubClient>()
                        .AddResilienceHandler("github", builder => { })
                        .AddResilienceHandler("github", builder => { });
                }
            }

            public sealed class GitHubClient
            {
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient<T>(this IServiceCollection services)
                {
                    return null!;
                }

                public static IHttpClientBuilder AddResilienceHandler(this IHttpClientBuilder builder, string name, System.Action<object> configure)
                {
                    return builder;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR040_StackedResilienceHandlersAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR040, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenDifferentNamedCustomResilienceHandlersAreUsed()
    {
        const string source = """
            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<GitHubClient>()
                        .AddResilienceHandler("read", builder => { })
                        .AddResilienceHandler("write", builder => { });
                }
            }

            public sealed class GitHubClient
            {
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient<T>(this IServiceCollection services)
                {
                    return null!;
                }

                public static IHttpClientBuilder AddResilienceHandler(this IHttpClientBuilder builder, string name, System.Action<object> configure)
                {
                    return builder;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR040_StackedResilienceHandlersAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenLookalikeNamedResilienceHandlerIsStackedOnCustomBuilder()
    {
        const string source = """
            public static class Registrations
            {
                public static CustomBuilder Configure(CustomBuilder builder)
                {
                    return builder
                        .AddResilienceHandler("github", item => { })
                        .AddResilienceHandler("github", item => { });
                }
            }

            public sealed class CustomBuilder
            {
            }

            public static class CustomBuilderExtensions
            {
                public static CustomBuilder AddResilienceHandler(this CustomBuilder builder, string name, System.Action<object> configure)
                {
                    return builder;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR040_StackedResilienceHandlersAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task CodeFix_RemovesDuplicateStandardResilienceHandler()
    {
        const string source = """
            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<GitHubClient>()
                        .AddStandardResilienceHandler()
                        .AddStandardResilienceHandler();
                }
            }

            public sealed class GitHubClient
            {
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient<T>(this IServiceCollection services)
                {
                    return null!;
                }

                public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder)
                {
                    return builder;
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR040_StackedResilienceHandlersAnalyzer, HCR040_RemoveDuplicateStandardResilienceHandlerCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Equal(1, CountOccurrences(fixedSource, ".AddStandardResilienceHandler()"));
    }

    [Fact]
    public async Task CodeFix_RemovesDuplicateNamedCustomResilienceHandler()
    {
        const string source = """
            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<GitHubClient>()
                        .AddResilienceHandler("github", builder => { })
                        .AddResilienceHandler("github", builder => { });
                }
            }

            public sealed class GitHubClient
            {
            }

            public interface IServiceCollection
            {
            }

            public interface IHttpClientBuilder
            {
            }

            public static class HttpClientBuilderExtensions
            {
                public static IHttpClientBuilder AddHttpClient<T>(this IServiceCollection services)
                {
                    return null!;
                }

                public static IHttpClientBuilder AddResilienceHandler(this IHttpClientBuilder builder, string name, System.Action<object> configure)
                {
                    return builder;
                }
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR040_StackedResilienceHandlersAnalyzer, HCR040_RemoveDuplicateStandardResilienceHandlerCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Equal(1, CountOccurrences(fixedSource, ".AddResilienceHandler(\"github\""));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
