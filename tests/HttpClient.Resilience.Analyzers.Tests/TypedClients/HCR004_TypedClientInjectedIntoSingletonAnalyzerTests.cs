using HttpClient.Resilience.Analyzers.Analyzers.TypedClients;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.TypedClients;

public sealed class HCR004_TypedClientInjectedIntoSingletonAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenSingletonConsumesTypedClient()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddSingleton<PaymentJob>();
                }
            }

            public sealed class PaymentsClient
            {
            }

            public sealed class PaymentJob(PaymentsClient paymentsClient)
            {
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR004, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenSingletonDoesNotConsumeTypedClient()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddSingleton<PaymentJob>();
                }
            }

            public sealed class PaymentsClient
            {
            }

            public sealed class PaymentJob
            {
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenRegistrationsAreSplitAcrossExtensionMethods()
    {
        const string source = """
            public static class Startup
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClients();
                    services.AddJobs();
                }
            }

            public static class RegistrationExtensions
            {
                public static IServiceCollection AddHttpClients(this IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    return services;
                }

                public static IServiceCollection AddJobs(this IServiceCollection services)
                {
                    services.AddSingleton<PaymentJob>();
                    return services;
                }
            }

            public sealed class PaymentsClient
            {
            }

            public sealed class PaymentJob(PaymentsClient paymentsClient)
            {
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR004, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenSingletonRegistrationUsesImplementationType()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<IPaymentsClient, PaymentsClient>();
                    services.AddSingleton<IPaymentJob, PaymentJob>();
                }
            }

            public interface IPaymentsClient
            {
            }

            public sealed class PaymentsClient : IPaymentsClient
            {
            }

            public interface IPaymentJob
            {
            }

            public sealed class PaymentJob(IPaymentsClient paymentsClient) : IPaymentJob
            {
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TService, TImplementation>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton<TService, TImplementation>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR004, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_InTraditionalStartupConfigureServices()
    {
        const string source = """
            public sealed class Startup
            {
                public void ConfigureServices(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddSingleton<PaymentJob>();
                }
            }

            public sealed class PaymentsClient
            {
            }

            public sealed class PaymentJob(PaymentsClient paymentsClient)
            {
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR004, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenRegistrationAndTypesAreInDifferentFiles()
    {
        const string registrations = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddSingleton<PaymentJob>();
                }
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;
            }
            """;

        const string services = """
            public sealed class PaymentsClient
            {
            }

            public sealed class PaymentJob(PaymentsClient paymentsClient)
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(registrations, services);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR004, diagnostic.Id);
    }
}
