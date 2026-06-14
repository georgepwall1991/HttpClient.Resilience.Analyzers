using HttpClient.Resilience.Analyzers.Analyzers.Handlers;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.Handlers;

public sealed class HCR020_DelegatingHandlerCapturesScopedDataAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenDelegatingHandlerCapturesHttpContextAccessor()
    {
        const string source = """
            using System.Net.Http;

            public sealed class UserHeaderHandler(IHttpContextAccessor accessor) : DelegatingHandler
            {
            }

            public interface IHttpContextAccessor
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR020_DelegatingHandlerCapturesScopedDataAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR020, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenDelegatingHandlerHasStatelessDependency()
    {
        const string source = """
            using System.Net.Http;

            public sealed class ApiKeyHandler(IApiKeyProvider provider) : DelegatingHandler
            {
            }

            public interface IApiKeyProvider
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR020_DelegatingHandlerCapturesScopedDataAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenDelegatingHandlerCapturesKnownScopedService()
    {
        const string source = """
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped<IUserContext, UserContext>();
                }
            }

            public sealed class UserHeaderHandler(IUserContext userContext) : DelegatingHandler
            {
            }

            public interface IUserContext
            {
            }

            public sealed class UserContext : IUserContext
            {
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddScoped<TService, TImplementation>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR020_DelegatingHandlerCapturesScopedDataAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR020, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenScopedRegistrationUsesQualifiedTypeName()
    {
        const string source = """
            using Contexts;
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped<Contexts.IUserContext, Contexts.UserContext>();
                }
            }

            public sealed class UserHeaderHandler(IUserContext userContext) : DelegatingHandler
            {
            }

            namespace Contexts
            {
                public interface IUserContext
                {
                }

                public sealed class UserContext : IUserContext
                {
                }
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddScoped<TService, TImplementation>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR020_DelegatingHandlerCapturesScopedDataAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR020, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenNullableQualifiedScopedServiceIsCaptured()
    {
        const string source = """
            #nullable enable

            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped<Contexts.IUserContext, Contexts.UserContext>();
                }
            }

            public sealed class UserHeaderHandler(Contexts.IUserContext? userContext) : DelegatingHandler
            {
            }

            namespace Contexts
            {
                public interface IUserContext
                {
                }

                public sealed class UserContext : IUserContext
                {
                }
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddScoped<TService, TImplementation>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR020_DelegatingHandlerCapturesScopedDataAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR020, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenQualifiedParameterTargetsDifferentSameNamedScopedType()
    {
        const string source = """
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped<Contexts.IUserContext, Contexts.UserContext>();
                }
            }

            public sealed class UserHeaderHandler(Other.IUserContext userContext) : DelegatingHandler
            {
            }

            namespace Contexts
            {
                public interface IUserContext
                {
                }

                public sealed class UserContext : IUserContext
                {
                }
            }

            namespace Other
            {
                public interface IUserContext
                {
                }
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddScoped<TService, TImplementation>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR020_DelegatingHandlerCapturesScopedDataAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenDelegatingHandlerCapturesKnownTransientService()
    {
        const string source = """
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddTransient<IApiKeyProvider, ApiKeyProvider>();
                }
            }

            public sealed class ApiKeyHandler(IApiKeyProvider provider) : DelegatingHandler
            {
            }

            public interface IApiKeyProvider
            {
            }

            public sealed class ApiKeyProvider : IApiKeyProvider
            {
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddTransient<TService, TImplementation>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR020_DelegatingHandlerCapturesScopedDataAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenScopedRegistrationAndHandlerAreInDifferentFiles()
    {
        const string registrations = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped<IUserContext, UserContext>();
                }
            }

            public interface IUserContext
            {
            }

            public sealed class UserContext : IUserContext
            {
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddScoped<TService, TImplementation>(this IServiceCollection services) => services;
            }
            """;

        const string handler = """
            using System.Net.Http;

            public sealed class UserHeaderHandler(UserContext userContext) : DelegatingHandler
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR020_DelegatingHandlerCapturesScopedDataAnalyzer>.GetDiagnosticsAsync(registrations, handler);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR020, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenLookalikeScopedRegistrationIsNotIServiceCollection()
    {
        const string source = """
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(CustomBuilder builder)
                {
                    builder.AddScoped<IUserContext, UserContext>();
                }
            }

            public sealed class UserHeaderHandler(IUserContext userContext) : DelegatingHandler
            {
            }

            public interface IUserContext
            {
            }

            public sealed class UserContext : IUserContext
            {
            }

            public sealed class CustomBuilder
            {
                public CustomBuilder AddScoped<TService, TImplementation>() => this;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR020_DelegatingHandlerCapturesScopedDataAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }
}
