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
    public async Task ReportsDiagnostic_WhenDelegatingHandlerCapturesQualifiedHttpContextAccessor()
    {
        const string source = """
            using System.Net.Http;

            public sealed class UserHeaderHandler(Microsoft.AspNetCore.Http.IHttpContextAccessor accessor) : DelegatingHandler
            {
            }

            namespace Microsoft.AspNetCore.Http
            {
                public interface IHttpContextAccessor
                {
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR020_DelegatingHandlerCapturesScopedDataAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR020, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenHandlerInheritsFromVisibleDelegatingHandlerBase()
    {
        const string source = """
            using System.Net.Http;

            public abstract class CorrelationHandlerBase : DelegatingHandler
            {
            }

            public sealed class UserHeaderHandler(IHttpContextAccessor accessor) : CorrelationHandlerBase
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
    public async Task ReportsDiagnostic_WhenDelegatingHandlerHasRequestScopedField()
    {
        const string source = """
            using System.Net.Http;

            public sealed class UserHeaderHandler : DelegatingHandler
            {
                private readonly IHttpContextAccessor _accessor = null!;
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
    public async Task ReportsDiagnostic_WhenDelegatingHandlerHasKnownScopedProperty()
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

            public sealed class UserHeaderHandler : DelegatingHandler
            {
                public IUserContext UserContext { get; set; } = null!;
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
    public async Task ReportsDiagnostic_WhenScopedRegistrationUsesAliasedServiceCollection()
    {
        const string source = """
            using System.Net.Http;
            using Services = global::IServiceCollection;

            public static class Registrations
            {
                public static void Configure(Services services)
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
    public async Task ReportsOnlyConstructorDiagnostic_WhenConstructorAndFieldCaptureSameScopedType()
    {
        const string source = """
            using System.Net.Http;

            public sealed class UserHeaderHandler(IHttpContextAccessor accessor) : DelegatingHandler
            {
                private readonly IHttpContextAccessor _accessor = accessor;
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
    public async Task ReportsDiagnostic_WhenScopedFactoryConstructsCapturedImplementation()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped<IUserContext>(sp => new UserContext());
                }
            }

            public sealed class UserHeaderHandler(UserContext userContext) : DelegatingHandler
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
                public static IServiceCollection AddScoped<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR020_DelegatingHandlerCapturesScopedDataAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR020, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTypeofScopedFactoryConstructsCapturedImplementation()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped(typeof(IUserContext), sp => new UserContext());
                }
            }

            public sealed class UserHeaderHandler(UserContext userContext) : DelegatingHandler
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
                public static IServiceCollection AddScoped(this IServiceCollection services, Type serviceType, Func<IServiceProvider, object> factory) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR020_DelegatingHandlerCapturesScopedDataAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR020, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenDelegatingHandlerCapturesScopedServiceFactory()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped<IUserContext, UserContext>();
                }
            }

            public sealed class UserHeaderHandler(Func<IUserContext> userContextFactory) : DelegatingHandler
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
    public async Task ReportsDiagnostic_WhenDelegatingHandlerCapturesLazyRequestScopedService()
    {
        const string source = """
            using System;
            using System.Net.Http;

            public sealed class UserHeaderHandler(Lazy<IHttpContextAccessor> accessor) : DelegatingHandler
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
    public async Task ReportsDiagnostic_WhenDelegatingHandlerCapturesScopedServiceCollection()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped<IUserContext, UserContext>();
                }
            }

            public sealed class UserHeaderHandler(IEnumerable<IUserContext> userContexts) : DelegatingHandler
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
    public async Task DoesNotReport_WhenQualifiedCustomWrapperContainsScopedServiceName()
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

            public sealed class UserHeaderHandler(Custom.Lazy<IUserContext> userContext) : DelegatingHandler
            {
            }

            public interface IUserContext
            {
            }

            public sealed class UserContext : IUserContext
            {
            }

            namespace Custom
            {
                public sealed class Lazy<T>
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
    public async Task DoesNotReport_WhenQualifiedBaseIsLookalikeDelegatingHandler()
    {
        const string source = """
            public sealed class UserHeaderHandler(IHttpContextAccessor accessor) : Custom.DelegatingHandler
            {
            }

            public interface IHttpContextAccessor
            {
            }

            namespace Custom
            {
                public abstract class DelegatingHandler
                {
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR020_DelegatingHandlerCapturesScopedDataAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResolvedSimpleBaseIsCustomDelegatingHandlerLookalike()
    {
        const string source = """
            public sealed class UserHeaderHandler(IHttpContextAccessor accessor) : DelegatingHandler
            {
            }

            public abstract class DelegatingHandler
            {
            }

            public interface IHttpContextAccessor
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR020_DelegatingHandlerCapturesScopedDataAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenQualifiedRequestScopedTypeNameIsCustomLookalike()
    {
        const string source = """
            using System.Net.Http;

            public sealed class UserHeaderHandler(Custom.HttpContext context) : DelegatingHandler
            {
            }

            namespace Custom
            {
                public sealed class HttpContext
                {
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR020_DelegatingHandlerCapturesScopedDataAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResolvedSimpleRequestScopedTypeNameIsCustomLookalike()
    {
        const string source = """
            using Custom;
            using System.Net.Http;

            public sealed class UserHeaderHandler(IHttpContextAccessor context) : DelegatingHandler
            {
            }

            namespace Custom
            {
                public interface IHttpContextAccessor
                {
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR020_DelegatingHandlerCapturesScopedDataAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenQualifiedCustomLookalikeIsRegisteredScoped()
    {
        const string source = """
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped<Custom.HttpContext>();
                }
            }

            public sealed class UserHeaderHandler(Custom.HttpContext context) : DelegatingHandler
            {
            }

            namespace Custom
            {
                public sealed class HttpContext
                {
                }
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddScoped<TService>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR020_DelegatingHandlerCapturesScopedDataAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR020, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenResolvedSimpleCustomLookalikeIsRegisteredScoped()
    {
        const string source = """
            using Custom;
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddScoped<Custom.IHttpContextAccessor>();
                }
            }

            public sealed class UserHeaderHandler(IHttpContextAccessor context) : DelegatingHandler
            {
            }

            namespace Custom
            {
                public interface IHttpContextAccessor
                {
                }
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddScoped<TService>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR020_DelegatingHandlerCapturesScopedDataAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR020, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenQualifiedBaseTargetsDifferentSameNamedHandlerBase()
    {
        const string source = """
            using System.Net.Http;

            namespace HttpPipeline
            {
                public abstract class CorrelationHandlerBase : DelegatingHandler
                {
                }
            }

            namespace Custom
            {
                public abstract class CorrelationHandlerBase
                {
                }
            }

            public sealed class UserHeaderHandler(IHttpContextAccessor accessor) : Custom.CorrelationHandlerBase
            {
            }

            public interface IHttpContextAccessor
            {
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

    [Fact]
    public async Task DoesNotReport_WhenLookalikeServicesParameterIsNotIServiceCollection()
    {
        const string source = """
            using System.Net.Http;

            public static class Registrations
            {
                public static void Configure(CustomServices services)
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

            public sealed class CustomServices
            {
                public CustomServices AddScoped<TService, TImplementation>() => this;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR020_DelegatingHandlerCapturesScopedDataAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenScopedRegistrationMethodIsOwnedByCustomNamespace()
    {
        const string source = """
            using System.Net.Http;
            using Custom.DependencyInjection;

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

            namespace Custom.DependencyInjection
            {
                public static class ServiceCollectionExtensions
                {
                    public static global::IServiceCollection AddScoped<TService, TImplementation>(
                        this global::IServiceCollection services) => services;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR020_DelegatingHandlerCapturesScopedDataAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }
}
