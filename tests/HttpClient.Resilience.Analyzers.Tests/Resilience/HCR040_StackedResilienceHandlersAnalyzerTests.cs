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
    public async Task DoesNotReport_WhenQualifiedLookalikeBuilderNameIsNotMicrosoftBuilder()
    {
        const string source = """
            public static class Registrations
            {
                public static Custom.IHttpClientBuilder Configure(Custom.IHttpClientBuilder builder)
                {
                    return builder
                        .AddStandardResilienceHandler()
                        .AddStandardResilienceHandler();
                }
            }

            namespace Custom
            {
                public interface IHttpClientBuilder
                {
                }

                public static class BuilderExtensions
                {
                    public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder)
                    {
                        return builder;
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR040_StackedResilienceHandlersAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenStandardResilienceHandlerIsRepeatedOnBuilderLocal()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    IHttpClientBuilder builder = services.AddHttpClient<GitHubClient>();
                    builder.AddStandardResilienceHandler();
                    builder.AddStandardResilienceHandler();
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
    public async Task DoesNotReport_WhenBuilderLocalIsReassignedBeforeSecondStandardHandler()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    IHttpClientBuilder builder = services.AddHttpClient<GitHubClient>();
                    builder.AddStandardResilienceHandler();

                    builder = services.AddHttpClient<PaymentsClient>();
                    builder.AddStandardResilienceHandler();
                }
            }

            public sealed class GitHubClient
            {
            }

            public sealed class PaymentsClient
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
    public async Task ReportsDiagnostic_WhenStandardHandlerIsRepeatedOnThisQualifiedBuilderField()
    {
        const string source = """
            public sealed class Registrations
            {
                private readonly IHttpClientBuilder _builder;

                public Registrations(IHttpClientBuilder builder)
                {
                    _builder = builder;
                }

                public void Configure()
                {
                    _builder.AddStandardResilienceHandler();
                    this._builder.AddStandardResilienceHandler();
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
    public async Task DoesNotReport_WhenRepeatedStandardHandlerUsesCustomBuilderLocal()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure()
                {
                    CustomBuilder builder = new();
                    builder.AddStandardResilienceHandler();
                    builder.AddStandardResilienceHandler();
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
    public async Task ReportsDiagnostic_WhenSameNamedCustomResilienceHandlerNameUsesConstant()
    {
        const string source = """
            public static class PipelineNames
            {
                public const string GitHub = "github";
            }

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<GitHubClient>()
                        .AddResilienceHandler(PipelineNames.GitHub, builder => { })
                        .AddResilienceHandler(PipelineNames.GitHub, builder => { });
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
    public async Task DoesNotReport_WhenCustomResilienceHandlerConstantsAreDifferent()
    {
        const string source = """
            public static class PipelineNames
            {
                public const string Read = "read";
                public const string Write = "write";
            }

            public static class Registrations
            {
                public static IHttpClientBuilder Configure(IServiceCollection services)
                {
                    return services
                        .AddHttpClient<GitHubClient>()
                        .AddResilienceHandler(PipelineNames.Read, builder => { })
                        .AddResilienceHandler(PipelineNames.Write, builder => { });
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

    [Fact]
    public async Task CodeFix_RemovesDuplicateStandaloneStandardResilienceHandlerStatement()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    IHttpClientBuilder builder = services.AddHttpClient<GitHubClient>();
                    builder.AddStandardResilienceHandler();
                    builder.AddStandardResilienceHandler();
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

        Assert.Equal(1, CountOccurrences(fixedSource, "builder.AddStandardResilienceHandler();"));
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
