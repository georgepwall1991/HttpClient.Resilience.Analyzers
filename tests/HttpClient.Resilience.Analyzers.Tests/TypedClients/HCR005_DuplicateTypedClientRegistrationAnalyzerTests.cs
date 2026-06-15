using HttpClient.Resilience.Analyzers.Analyzers.TypedClients;
using HttpClient.Resilience.Analyzers.CodeFixes;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.TypedClients;

public sealed class HCR005_DuplicateTypedClientRegistrationAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenTypedClientIsSeparatelyRegistered()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddTransient<PaymentsClient>();
                }
            }

            public sealed class PaymentsClient
            {
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddTransient<TService>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR005_DuplicateTypedClientRegistrationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR005, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenOnlyTypedClientRegistrationExists()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                }
            }

            public sealed class PaymentsClient
            {
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR005_DuplicateTypedClientRegistrationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenDuplicateRegistrationIsInSeparateExtensionMethod()
    {
        const string source = """
            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClients();
                    services.AddOtherServices();
                }
            }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddHttpClients(this IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    return services;
                }

                public static IServiceCollection AddOtherServices(this IServiceCollection services)
                {
                    services.AddScoped<PaymentsClient>();
                    return services;
                }
            }

            public sealed class PaymentsClient
            {
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddScoped<TService>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR005_DuplicateTypedClientRegistrationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR005, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenDuplicateRegistrationUsesServiceAndImplementationTypes()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<IPaymentsClient, PaymentsClient>();
                    services.AddTransient<IPaymentsClient, PaymentsClient>();
                }
            }

            public interface IPaymentsClient
            {
            }

            public sealed class PaymentsClient : IPaymentsClient
            {
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TService, TImplementation>(this IServiceCollection services) => services;
                public static IServiceCollection AddTransient<TService, TImplementation>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR005_DuplicateTypedClientRegistrationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR005, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenDuplicateRegistrationUsesTypeofServiceType()
    {
        const string source = """
            using System;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddTransient(typeof(PaymentsClient));
                }
            }

            public sealed class PaymentsClient
            {
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddTransient(this IServiceCollection services, Type serviceType) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR005_DuplicateTypedClientRegistrationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR005, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenDuplicateRegistrationUsesTypeofServiceAndImplementationTypes()
    {
        const string source = """
            using System;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<IPaymentsClient, PaymentsClient>();
                    services.AddTransient(typeof(IPaymentsClient), typeof(PaymentsClient));
                }
            }

            public interface IPaymentsClient
            {
            }

            public sealed class PaymentsClient : IPaymentsClient
            {
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TService, TImplementation>(this IServiceCollection services) => services;
                public static IServiceCollection AddTransient(this IServiceCollection services, Type serviceType, Type implementationType) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR005_DuplicateTypedClientRegistrationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR005, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenDuplicateRegistrationUsesTypeofFactory()
    {
        const string source = """
            using System;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddTransient(typeof(PaymentsClient), sp => new PaymentsClient());
                }
            }

            public sealed class PaymentsClient
            {
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddTransient(this IServiceCollection services, Type serviceType, Func<IServiceProvider, object> factory) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR005_DuplicateTypedClientRegistrationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR005, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenDuplicateRegistrationFactoryConstructsTypedClient()
    {
        const string source = """
            using System;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddTransient<IPaymentsClient>(sp => new PaymentsClient());
                }
            }

            public interface IPaymentsClient
            {
            }

            public sealed class PaymentsClient : IPaymentsClient
            {
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddTransient<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR005_DuplicateTypedClientRegistrationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR005, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenDuplicateRegistrationTwoGenericFactoryConstructsTypedClient()
    {
        const string source = """
            using System;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<IPaymentsClient, PaymentsClient>();
                    services.AddTransient<IPaymentsClient>(sp => new PaymentsClient());
                }
            }

            public interface IPaymentsClient
            {
            }

            public sealed class PaymentsClient : IPaymentsClient
            {
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TService, TImplementation>(this IServiceCollection services) => services;
                public static IServiceCollection AddTransient<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR005_DuplicateTypedClientRegistrationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR005, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenDuplicateRegistrationUsesQualifiedTypeName()
    {
        const string source = """
            using Clients;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<Clients.PaymentsClient>();
                    services.AddTransient<PaymentsClient>();
                }
            }

            namespace Clients
            {
                public sealed class PaymentsClient
                {
                }
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddTransient<TService>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR005_DuplicateTypedClientRegistrationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR005, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenQualifiedDuplicateTargetsDifferentSameNamedType()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<Clients.PaymentsClient>();
                    services.AddTransient<Other.PaymentsClient>();
                }
            }

            namespace Clients
            {
                public sealed class PaymentsClient
                {
                }
            }

            namespace Other
            {
                public sealed class PaymentsClient
                {
                }
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddTransient<TService>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR005_DuplicateTypedClientRegistrationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenTypeofDuplicateTargetsDifferentSameNamedType()
    {
        const string source = """
            using System;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<Clients.PaymentsClient>();
                    services.AddTransient(typeof(Other.PaymentsClient));
                }
            }

            namespace Clients
            {
                public sealed class PaymentsClient
                {
                }
            }

            namespace Other
            {
                public sealed class PaymentsClient
                {
                }
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddTransient(this IServiceCollection services, Type serviceType) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR005_DuplicateTypedClientRegistrationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_InMinimalHostingStyleConfiguration()
    {
        const string source = """
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddHttpClient<PaymentsClient>();
            builder.Services.AddTransient<PaymentsClient>();

            public sealed class PaymentsClient
            {
            }

            public sealed class WebApplication
            {
                public IServiceCollection Services { get; } = null!;
                public static WebApplication CreateBuilder(string[] args) => null!;
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddTransient<TService>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR005_DuplicateTypedClientRegistrationAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR005, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTypedClientAndDuplicateRegistrationAreInDifferentFiles()
    {
        const string httpClients = """
            public static class HttpClientRegistrations
            {
                public static IServiceCollection AddHttpClients(this IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    return services;
                }
            }

            public sealed class PaymentsClient
            {
            }

            public interface IServiceCollection
            {
            }

            public static class HttpClientRegistrationExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
            }
            """;

        const string services = """
            public static class ServiceRegistrations
            {
                public static IServiceCollection AddServices(this IServiceCollection services)
                {
                    services.AddTransient<PaymentsClient>();
                    return services;
                }
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddTransient<TService>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR005_DuplicateTypedClientRegistrationAnalyzer>.GetDiagnosticsAsync(httpClients, services);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR005, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenLookalikeRegistrationsAreNotIServiceCollection()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(CustomBuilder builder)
                {
                    builder.AddHttpClient<PaymentsClient>();
                    builder.AddTransient<PaymentsClient>();
                }
            }

            public sealed class PaymentsClient
            {
            }

            public sealed class CustomBuilder
            {
                public CustomBuilder AddHttpClient<TClient>() => this;
                public CustomBuilder AddTransient<TService>() => this;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR005_DuplicateTypedClientRegistrationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenLookalikeServicesParameterIsNotIServiceCollection()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(CustomServices services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddTransient<PaymentsClient>();
                }
            }

            public sealed class PaymentsClient
            {
            }

            public sealed class CustomServices
            {
                public CustomServices AddHttpClient<TClient>() => this;
                public CustomServices AddTransient<TService>() => this;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR005_DuplicateTypedClientRegistrationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenLookalikeServicesPropertyIsNotIServiceCollection()
    {
        const string source = """
            var builder = CustomApplication.CreateBuilder(args);

            builder.Services.AddHttpClient<PaymentsClient>();
            builder.Services.AddTransient<PaymentsClient>();

            public sealed class PaymentsClient
            {
            }

            public sealed class CustomApplication
            {
                public CustomServices Services { get; } = new();
                public static CustomApplication CreateBuilder(string[] args) => new();
            }

            public sealed class CustomServices
            {
                public CustomServices AddHttpClient<TClient>() => this;
                public CustomServices AddTransient<TService>() => this;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR005_DuplicateTypedClientRegistrationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenUnresolvedBuilderServicesSharesServiceCollectionPropertyName()
    {
        const string source = """
            var builder = UnknownApplication.CreateBuilder(args);

            builder.Services.AddHttpClient<PaymentsClient>();
            builder.Services.AddTransient<PaymentsClient>();

            public sealed class PaymentsClient
            {
            }

            public sealed class RealApplication
            {
                public IServiceCollection Services { get; } = null!;
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddTransient<TService>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR005_DuplicateTypedClientRegistrationAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task CodeFix_RemovesDuplicateRegistrationStatement()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddTransient<PaymentsClient>();
                }
            }

            public sealed class PaymentsClient
            {
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddTransient<TService>(this IServiceCollection services) => services;
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR005_DuplicateTypedClientRegistrationAnalyzer, HCR005_RemoveDuplicateTypedClientRegistrationCodeFixProvider>
            .ApplyFirstCodeFixAsync(source);

        Assert.Contains("services.AddHttpClient<PaymentsClient>();", fixedSource);
        Assert.DoesNotContain("services.AddTransient<PaymentsClient>();", fixedSource);
    }
}
