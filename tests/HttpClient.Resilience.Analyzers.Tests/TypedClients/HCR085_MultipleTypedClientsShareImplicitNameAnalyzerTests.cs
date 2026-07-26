using HttpClient.Resilience.Analyzers.Analyzers.TypedClients;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;
using Microsoft.CodeAnalysis;

namespace HttpClient.Resilience.Analyzers.Tests.TypedClients;

public sealed class HCR085_MultipleTypedClientsShareImplicitNameAnalyzerTests
{
    private const string Framework = """
        using System;
        using System.Net.Http;

        public interface IServiceCollection
        {
        }

        public interface IHttpClientBuilder
        {
        }

        public static class ServiceCollectionExtensions
        {
            public static IHttpClientBuilder AddHttpClient<TService, TImplementation>(
                this IServiceCollection services)
                where TImplementation : TService =>
                default!;

            public static IHttpClientBuilder AddHttpClient<TService, TImplementation>(
                this IServiceCollection services,
                Action<HttpClient> configureClient)
                where TImplementation : TService =>
                default!;

            public static IHttpClientBuilder AddHttpClient<TService, TImplementation>(
                this IServiceCollection services,
                string name)
                where TImplementation : TService =>
                default!;

            public static IHttpClientBuilder AddHttpClient<TService, TImplementation>(
                this IServiceCollection services,
                string name,
                Action<HttpClient> configureClient)
                where TImplementation : TService =>
                default!;

            public static IHttpClientBuilder AddHttpClient<TClient>(
                this IServiceCollection services) =>
                default!;
        }
        """;

    [Fact]
    public async Task ReportsDiagnostic_WhenDifferentImplementationsShareImplicitServiceName()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<IPaymentsClient, StripePaymentsClient>();
                    services.AddHttpClient<IPaymentsClient, AdyenPaymentsClient>();
                }
            }

            public interface IPaymentsClient
            {
            }

            public sealed class StripePaymentsClient : IPaymentsClient
            {
            }

            public sealed class AdyenPaymentsClient : IPaymentsClient
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR085_MultipleTypedClientsShareImplicitNameAnalyzer>
            .GetDiagnosticsAsync(source, Framework);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR085, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("StripePaymentsClient", diagnostic.GetMessage());
        Assert.Contains("AdyenPaymentsClient", diagnostic.GetMessage());
        Assert.Contains("IPaymentsClient", diagnostic.GetMessage());
        Assert.Equal("Test.cs", diagnostic.Location.SourceTree?.FilePath);
        Assert.Single(diagnostic.AdditionalLocations);
        Assert.Equal("Test.cs", diagnostic.AdditionalLocations[0].SourceTree?.FilePath);
        Assert.True(
            diagnostic.AdditionalLocations[0].SourceSpan.Start <
            diagnostic.Location.SourceSpan.Start);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenConfiguredRegistrationsShareImplicitServiceName()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<IPaymentsClient, StripePaymentsClient>(
                        client => client.BaseAddress = new("https://stripe.example"));
                    services.AddHttpClient<IPaymentsClient, AdyenPaymentsClient>(
                        client => client.BaseAddress = new("https://adyen.example"));
                }
            }

            public interface IPaymentsClient
            {
            }

            public sealed class StripePaymentsClient : IPaymentsClient
            {
            }

            public sealed class AdyenPaymentsClient : IPaymentsClient
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR085_MultipleTypedClientsShareImplicitNameAnalyzer>
            .GetDiagnosticsAsync(source, Framework);

        Assert.Single(diagnostics);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenCollisionSpansSourceFiles()
    {
        const string firstRegistration = """
            public static class StripeRegistration
            {
                public static void ConfigureStripe(IServiceCollection services)
                {
                    services.AddHttpClient<IPaymentsClient, StripePaymentsClient>();
                }
            }

            public interface IPaymentsClient
            {
            }

            public sealed class StripePaymentsClient : IPaymentsClient
            {
            }
            """;

        const string secondRegistration = """
            public static class AdyenRegistration
            {
                public static void ConfigureAdyen(IServiceCollection services)
                {
                    services.AddHttpClient<IPaymentsClient, AdyenPaymentsClient>();
                }
            }

            public sealed class AdyenPaymentsClient : IPaymentsClient
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR085_MultipleTypedClientsShareImplicitNameAnalyzer>
            .GetDiagnosticsAsync(firstRegistration, secondRegistration, Framework);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("Test1.cs", diagnostic.Location.SourceTree?.FilePath);
        Assert.Equal("Test.cs", Assert.Single(diagnostic.AdditionalLocations).SourceTree?.FilePath);
    }

    [Fact]
    public async Task ReportsEachRegistrationAfterTheFirstCollision()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<IPaymentsClient, StripePaymentsClient>();
                    services.AddHttpClient<IPaymentsClient, AdyenPaymentsClient>();
                    services.AddHttpClient<IPaymentsClient, CheckoutPaymentsClient>();
                }
            }

            public interface IPaymentsClient
            {
            }

            public sealed class StripePaymentsClient : IPaymentsClient
            {
            }

            public sealed class AdyenPaymentsClient : IPaymentsClient
            {
            }

            public sealed class CheckoutPaymentsClient : IPaymentsClient
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR085_MultipleTypedClientsShareImplicitNameAnalyzer>
            .GetDiagnosticsAsync(source, Framework);

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal(DiagnosticIds.HCR085, diagnostic.Id));
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenMinimalHostingServicesShareImplicitServiceName()
    {
        const string source = """
            var builder = Application.CreateBuilder();

            builder.Services.AddHttpClient<IPaymentsClient, StripePaymentsClient>();
            builder.Services.AddHttpClient<IPaymentsClient, AdyenPaymentsClient>();

            public sealed class Application
            {
                public IServiceCollection Services { get; } = default!;

                public static Application CreateBuilder() => new();
            }

            public interface IPaymentsClient
            {
            }

            public sealed class StripePaymentsClient : IPaymentsClient
            {
            }

            public sealed class AdyenPaymentsClient : IPaymentsClient
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR085_MultipleTypedClientsShareImplicitNameAnalyzer>
            .GetDiagnosticsAsync(source, Framework);

        Assert.Single(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenRegistrationsUseExplicitNames()
    {
        const string source = """
            using System;

            public static class Registrations
            {
                private const string AdyenName = "adyen";

                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<IPaymentsClient, StripePaymentsClient>(
                        name: "stripe");
                    services.AddHttpClient<IPaymentsClient, AdyenPaymentsClient>(
                        AdyenName,
                        client => client.BaseAddress = new Uri("https://adyen.example"));
                }
            }

            public interface IPaymentsClient
            {
            }

            public sealed class StripePaymentsClient : IPaymentsClient
            {
            }

            public sealed class AdyenPaymentsClient : IPaymentsClient
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR085_MultipleTypedClientsShareImplicitNameAnalyzer>
            .GetDiagnosticsAsync(source, Framework);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenSameImplementationIsRegisteredRepeatedly()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<IPaymentsClient, PaymentsClient>();
                    services.AddHttpClient<IPaymentsClient, PaymentsClient>();
                }
            }

            public interface IPaymentsClient
            {
            }

            public sealed class PaymentsClient : IPaymentsClient
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR085_MultipleTypedClientsShareImplicitNameAnalyzer>
            .GetDiagnosticsAsync(source, Framework);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenImplementationsUseDifferentServiceTypes()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<IPaymentsClient, PaymentsClient>();
                    services.AddHttpClient<IOrdersClient, OrdersClient>();
                }
            }

            public interface IPaymentsClient
            {
            }

            public interface IOrdersClient
            {
            }

            public sealed class PaymentsClient : IPaymentsClient
            {
            }

            public sealed class OrdersClient : IOrdersClient
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR085_MultipleTypedClientsShareImplicitNameAnalyzer>
            .GetDiagnosticsAsync(source, Framework);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenRegistrationsUseOneGenericArgument()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<StripePaymentsClient>();
                    services.AddHttpClient<AdyenPaymentsClient>();
                }
            }

            public sealed class StripePaymentsClient
            {
            }

            public sealed class AdyenPaymentsClient
            {
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR085_MultipleTypedClientsShareImplicitNameAnalyzer>
            .GetDiagnosticsAsync(source, Framework);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenSameSimpleServiceNameResolvesToDifferentTypes()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<Payments.IPaymentsClient, Payments.PaymentsClient>();
                    services.AddHttpClient<Orders.IPaymentsClient, Orders.PaymentsClient>();
                }
            }

            namespace Payments
            {
                public interface IPaymentsClient
                {
                }

                public sealed class PaymentsClient : IPaymentsClient
                {
                }
            }

            namespace Orders
            {
                public interface IPaymentsClient
                {
                }

                public sealed class PaymentsClient : IPaymentsClient
                {
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR085_MultipleTypedClientsShareImplicitNameAnalyzer>
            .GetDiagnosticsAsync(source, Framework);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenAddHttpClientIsACustomLookalike()
    {
        const string source = """
            using Custom.DependencyInjection;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<IPaymentsClient, StripePaymentsClient>();
                    services.AddHttpClient<IPaymentsClient, AdyenPaymentsClient>();
                }
            }

            public interface IServiceCollection
            {
            }

            public interface IPaymentsClient
            {
            }

            public sealed class StripePaymentsClient : IPaymentsClient
            {
            }

            public sealed class AdyenPaymentsClient : IPaymentsClient
            {
            }

            namespace Custom.DependencyInjection
            {
                public static class ServiceCollectionExtensions
                {
                    public static IServiceCollection AddHttpClient<TService, TImplementation>(
                        this IServiceCollection services)
                        where TImplementation : TService =>
                        services;
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR085_MultipleTypedClientsShareImplicitNameAnalyzer>
            .GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }
}
