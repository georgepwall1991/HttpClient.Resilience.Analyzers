using HttpClient.Resilience.Analyzers.Analyzers.TypedClients;
using HttpClient.Resilience.Analyzers.CodeFixes;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;
using Xunit;

namespace HttpClient.Resilience.Analyzers.Tests.TypedClients;

public sealed class HCR085_AddExplicitClientNameCodeFixProviderTests
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
                string name)
                where TImplementation : TService =>
                default!;
        }
        """;

    private const string TwoConflictingRegistrations = """
        public static class Registrations
        {
            public static void Configure(IServiceCollection services)
            {
                services.AddHttpClient<IPaymentsClient, StripePaymentsClient>();
                services.AddHttpClient<IPaymentsClient, AdyenPaymentsClient>();
                services.AddHttpClient<IAuditClient, InternalAuditClient>();
                services.AddHttpClient<IAuditClient, ExternalAuditClient>();
            }
        }

        public interface IPaymentsClient
        {
        }

        public interface IAuditClient
        {
        }

        public sealed class StripePaymentsClient : IPaymentsClient
        {
        }

        public sealed class AdyenPaymentsClient : IPaymentsClient
        {
        }

        public sealed class InternalAuditClient : IAuditClient
        {
        }

        public sealed class ExternalAuditClient : IAuditClient
        {
        }
        """;

    private const string SingleConflictingPair = """
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

    [Fact]
    public async Task CodeFix_AddsExplicitNameDerivedFromImplementationType()
    {
        var fixedSource = await CodeFixVerifier<HCR085_MultipleTypedClientsShareImplicitNameAnalyzer, HCR085_AddExplicitClientNameCodeFixProvider>
            .ApplyFirstCodeFixAsync(Framework + SingleConflictingPair);

        Assert.Contains("\"adyen-payments-client\"", fixedSource, System.StringComparison.Ordinal);

        // Naming the flagged registration resolves the shared-implicit-name conflict.
        Assert.Empty(await AnalyzerVerifier<HCR085_MultipleTypedClientsShareImplicitNameAnalyzer>
            .GetDiagnosticsAsync(fixedSource));
    }

    [Fact]
    public async Task CodeFix_FixAllAddsDistinctNamesToBothRegistrations()
    {
        var diagnosticsBefore = await AnalyzerVerifier<HCR085_MultipleTypedClientsShareImplicitNameAnalyzer>
            .GetDiagnosticsAsync(TwoConflictingRegistrations, Framework);
        Assert.Equal(2, diagnosticsBefore.Length);

        var fixedSource = await CodeFixVerifier<HCR085_MultipleTypedClientsShareImplicitNameAnalyzer, HCR085_AddExplicitClientNameCodeFixProvider>
            .ApplyFixAllInDocumentAsync(Framework + TwoConflictingRegistrations);

        Assert.DoesNotContain("\"stripe-payments-client\"", fixedSource, System.StringComparison.Ordinal);
        Assert.Contains("\"adyen-payments-client\"", fixedSource, System.StringComparison.Ordinal);
        Assert.Empty(await AnalyzerVerifier<HCR085_MultipleTypedClientsShareImplicitNameAnalyzer>
            .GetDiagnosticsAsync(fixedSource));
    }

    [Fact]
    public async Task ToKebabCase_SplitsPascalAndAcronyms()
    {
        Assert.Equal("stripe-payments-client", HCR085_AddExplicitClientNameCodeFixProvider.ToKebabCase("StripePaymentsClient"));
        Assert.Equal("http-api-client", HCR085_AddExplicitClientNameCodeFixProvider.ToKebabCase("HttpApiClient"));
        Assert.Equal("audit-client", HCR085_AddExplicitClientNameCodeFixProvider.ToKebabCase("AuditClient"));
    }
}
