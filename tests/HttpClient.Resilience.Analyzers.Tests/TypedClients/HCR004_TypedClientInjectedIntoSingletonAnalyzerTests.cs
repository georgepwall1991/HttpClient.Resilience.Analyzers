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
    public async Task ReportsDiagnostic_WhenSingletonConsumesNullableTypedClient()
    {
        const string source = """
            #nullable enable

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

            public sealed class PaymentJob(PaymentsClient? paymentsClient)
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
    public async Task ReportsDiagnostic_WhenSingletonConsumesLazyTypedClient()
    {
        const string source = """
            using System;

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

            public sealed class PaymentJob(Lazy<PaymentsClient> paymentsClient)
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
    public async Task ReportsDiagnostic_WhenSingletonConsumesTypedClientCollection()
    {
        const string source = """
            using System.Collections.Generic;

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

            public sealed class PaymentJob(IEnumerable<PaymentsClient> paymentsClients)
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
    public async Task ReportsDiagnostic_WhenSingletonFactoryResolvesTypedClient()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddSingleton<PaymentJob>(sp => PaymentJob.Create(sp.GetRequiredService<PaymentsClient>()));
                }
            }

            public sealed class PaymentsClient
            {
            }

            public sealed class PaymentJob
            {
                public static PaymentJob Create(PaymentsClient paymentsClient) => new();
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services, System.Func<System.IServiceProvider, TService> factory) => services;
            }

            public static class ServiceProviderExtensions
            {
                public static TService GetRequiredService<TService>(this System.IServiceProvider provider) => default!;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR004, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenCustomServiceProviderExtensionResolvesTypedClient()
    {
        const string source = """
            using CustomResolution;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddSingleton<PaymentJob>(sp => PaymentJob.Create(sp.GetRequiredService<PaymentsClient>()));
                }
            }

            public sealed class PaymentsClient
            {
            }

            public sealed class PaymentJob
            {
                public static PaymentJob Create(PaymentsClient paymentsClient) => new();
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services, System.Func<System.IServiceProvider, TService> factory) => services;
            }

            namespace CustomResolution
            {
                public static class ServiceProviderExtensions
                {
                    public static TService GetRequiredService<TService>(this System.IServiceProvider provider) => default!;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTypeofSingletonFactoryResolvesTypedClient()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddSingleton(typeof(PaymentJob), sp => PaymentJob.Create(sp.GetRequiredService<PaymentsClient>()));
                }
            }

            public sealed class PaymentsClient
            {
            }

            public sealed class PaymentJob
            {
                public static PaymentJob Create(PaymentsClient paymentsClient) => new();
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton(this IServiceCollection services, System.Type serviceType, System.Func<System.IServiceProvider, object> factory) => services;
            }

            public static class ServiceProviderExtensions
            {
                public static TService GetRequiredService<TService>(this System.IServiceProvider provider) => default!;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR004, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenSingletonAnonymousFactoryResolvesTypedClient()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddSingleton<PaymentJob>(delegate (System.IServiceProvider sp)
                    {
                        return PaymentJob.Create(sp.GetRequiredService<PaymentsClient>());
                    });
                }
            }

            public sealed class PaymentsClient
            {
            }

            public sealed class PaymentJob
            {
                public static PaymentJob Create(PaymentsClient paymentsClient) => new();
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services, System.Func<System.IServiceProvider, TService> factory) => services;
            }

            public static class ServiceProviderExtensions
            {
                public static TService GetRequiredService<TService>(this System.IServiceProvider provider) => default!;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR004, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenSingletonFactoryConstructsImplementationThatConsumesTypedClient()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddSingleton<IPaymentJob>(sp => new PaymentJob(default!));
                }
            }

            public sealed class PaymentsClient
            {
            }

            public interface IPaymentJob
            {
            }

            public sealed class PaymentJob(PaymentsClient paymentsClient) : IPaymentJob
            {
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services, System.Func<System.IServiceProvider, TService> factory) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR004, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenTypeofSingletonFactoryConstructsImplementationThatConsumesTypedClient()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddSingleton(typeof(IPaymentJob), sp => new PaymentJob(default!));
                }
            }

            public sealed class PaymentsClient
            {
            }

            public interface IPaymentJob
            {
            }

            public sealed class PaymentJob(PaymentsClient paymentsClient) : IPaymentJob
            {
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton(this IServiceCollection services, System.Type serviceType, System.Func<System.IServiceProvider, object> factory) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR004, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenSingletonFactoryResolvesLazyTypedClient()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddSingleton<PaymentJob>(sp => PaymentJob.Create(sp.GetRequiredService<System.Lazy<PaymentsClient>>()));
                }
            }

            public sealed class PaymentsClient
            {
            }

            public sealed class PaymentJob
            {
                public static PaymentJob Create(System.Lazy<PaymentsClient> paymentsClient) => new();
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services, System.Func<System.IServiceProvider, TService> factory) => services;
            }

            public static class ServiceProviderExtensions
            {
                public static TService GetRequiredService<TService>(this System.IServiceProvider provider) => default!;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR004, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenSingletonFactoryUsesTypedServiceProviderParameter()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddSingleton<PaymentJob>((System.IServiceProvider factory) =>
                        PaymentJob.Create(factory.GetService<PaymentsClient>()));
                }
            }

            public sealed class PaymentsClient
            {
            }

            public sealed class PaymentJob
            {
                public static PaymentJob Create(PaymentsClient? paymentsClient) => new();
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services, System.Func<System.IServiceProvider, TService> factory) => services;
            }

            public static class ServiceProviderExtensions
            {
                public static TService? GetService<TService>(this System.IServiceProvider provider) => default;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR004, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenSingletonFactoryUsesAliasedServiceProviderParameter()
    {
        const string source = """
            using Provider = System.IServiceProvider;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddSingleton<PaymentJob>((Provider factory) =>
                        PaymentJob.Create(factory.GetService<PaymentsClient>()));
                }
            }

            public sealed class PaymentsClient
            {
            }

            public sealed class PaymentJob
            {
                public static PaymentJob Create(PaymentsClient? paymentsClient) => new();
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services, System.Func<System.IServiceProvider, TService> factory) => services;
            }

            public static class ServiceProviderExtensions
            {
                public static TService? GetService<TService>(this System.IServiceProvider provider) => default;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR004, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenTypedFactoryParameterIsCustomServiceProviderLookalike()
    {
        const string source = """
            using Provider = Custom.IServiceProvider;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddSingleton<PaymentJob>((Provider factory) =>
                        PaymentJob.Create(factory.GetService<PaymentsClient>()));
                }
            }

            public sealed class PaymentsClient
            {
            }

            public sealed class PaymentJob
            {
                public static PaymentJob Create(PaymentsClient? paymentsClient) => new();
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services, System.Func<Provider, TService> factory) => services;
            }

            public static class ServiceProviderExtensions
            {
                public static TService? GetService<TService>(this Provider provider) => default;
            }

            namespace Custom
            {
                public interface IServiceProvider
                {
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenSingletonFactoryResolvesNonTypedService()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddSingleton<PaymentJob>(sp => PaymentJob.Create(sp.GetRequiredService<Clock>()));
                }
            }

            public sealed class PaymentsClient
            {
            }

            public sealed class Clock
            {
            }

            public sealed class PaymentJob
            {
                public static PaymentJob Create(Clock clock) => new();
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services, System.Func<System.IServiceProvider, TService> factory) => services;
            }

            public static class ServiceProviderExtensions
            {
                public static TService GetRequiredService<TService>(this System.IServiceProvider provider) => default!;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenFactoryReceiverIsNotServiceProviderParameter()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddSingleton<PaymentJob>(factory => PaymentJob.Create(factory.GetRequiredService<PaymentsClient>()));
                }
            }

            public sealed class PaymentsClient
            {
            }

            public sealed class PaymentJob
            {
                public static PaymentJob Create(PaymentsClient paymentsClient) => new();
            }

            public interface IServiceCollection
            {
            }

            public sealed class CustomFactory
            {
                public TService GetRequiredService<TService>() => default!;
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services, System.Func<CustomFactory, TService> factory) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenInferredFactoryParameterNamedSpHasCustomType()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddSingleton<PaymentJob>(sp => PaymentJob.Create(sp.GetRequiredService<PaymentsClient>()));
                }
            }

            public sealed class PaymentsClient
            {
            }

            public sealed class PaymentJob
            {
                public static PaymentJob Create(PaymentsClient paymentsClient) => new();
            }

            public interface IServiceCollection
            {
            }

            public sealed class CustomFactory
            {
                public TService GetRequiredService<TService>() => default!;
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services, System.Func<CustomFactory, TService> factory) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenAnonymousFactoryReceiverIsNotServiceProviderParameter()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddSingleton<PaymentJob>(delegate (CustomFactory factory)
                    {
                        return PaymentJob.Create(factory.GetRequiredService<PaymentsClient>());
                    });
                }
            }

            public sealed class PaymentsClient
            {
            }

            public sealed class PaymentJob
            {
                public static PaymentJob Create(PaymentsClient paymentsClient) => new();
            }

            public interface IServiceCollection
            {
            }

            public sealed class CustomFactory
            {
                public TService GetRequiredService<TService>() => default!;
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services, System.Func<CustomFactory, TService> factory) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenSingletonConsumesCustomTypedClientWrapper()
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

            public sealed class PaymentJob(Custom.Lazy<PaymentsClient> paymentsClient)
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
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
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
    public async Task ReportsDiagnostic_WhenRegistrationsUseQualifiedTypeNames()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<Clients.PaymentsClient>();
                    services.AddSingleton<Jobs.PaymentJob>();
                }
            }

            namespace Clients
            {
                public sealed class PaymentsClient
                {
                }
            }

            namespace Jobs
            {
                using Clients;

                public sealed class PaymentJob(PaymentsClient paymentsClient)
                {
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

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR004, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenUnqualifiedTypedClientRegistrationResolvesToConstructorType()
    {
        const string source = """
            namespace Clients
            {
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddHttpClient<PaymentsClient>();
                        services.AddSingleton<Jobs.PaymentJob>();
                    }
                }

                public sealed class PaymentsClient
                {
                }
            }

            namespace Jobs
            {
                using Clients;

                public sealed class PaymentJob(PaymentsClient paymentsClient)
                {
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

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR004, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenQualifiedTypedClientRegistrationTargetsDifferentSameNamedConstructorType()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<Clients.PaymentsClient>();
                    services.AddSingleton<Jobs.PaymentJob>();
                }
            }

            namespace Clients
            {
                public sealed class PaymentsClient
                {
                }
            }

            namespace Jobs
            {
                public sealed class PaymentsClient
                {
                }

                public sealed class PaymentJob(PaymentsClient paymentsClient)
                {
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

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenQualifiedSingletonRegistrationTargetsDifferentSameNamedType()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<Clients.PaymentsClient>();
                    services.AddSingleton<Jobs.PaymentJob>();
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
                using Clients;

                public sealed class PaymentJob(PaymentsClient paymentsClient)
                {
                }
            }

            namespace Jobs
            {
                public sealed class PaymentJob
                {
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

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
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

    [Fact]
    public async Task DoesNotReport_WhenLookalikeRegistrationsAreNotIServiceCollection()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(CustomBuilder builder)
                {
                    builder.AddHttpClient<PaymentsClient>();
                    builder.AddSingleton<PaymentJob>();
                }
            }

            public sealed class PaymentsClient
            {
            }

            public sealed class PaymentJob(PaymentsClient paymentsClient)
            {
            }

            public sealed class CustomBuilder
            {
                public CustomBuilder AddHttpClient<TClient>() => this;
                public CustomBuilder AddSingleton<TService>() => this;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

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
                    services.AddSingleton<PaymentJob>();
                }
            }

            public sealed class PaymentsClient
            {
            }

            public sealed class PaymentJob(PaymentsClient paymentsClient)
            {
            }

            public sealed class CustomServices
            {
                public CustomServices AddHttpClient<TClient>() => this;
                public CustomServices AddSingleton<TService>() => this;
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }
}
