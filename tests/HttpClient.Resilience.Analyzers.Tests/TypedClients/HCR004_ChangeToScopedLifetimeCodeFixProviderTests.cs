using HttpClient.Resilience.Analyzers.Analyzers.TypedClients;
using HttpClient.Resilience.Analyzers.CodeFixes;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;
using Xunit;

namespace HttpClient.Resilience.Analyzers.Tests.TypedClients;

public sealed class HCR004_ChangeToScopedLifetimeCodeFixProviderTests
{
    private const string Fixture = """
        public static class Registrations
        {
            public static void Configure(IServiceCollection services)
            {
                services.AddHttpClient<PaymentsClient>();
                services.AddSingleton<PaymentJob>();
                services.AddScoped<Auditor>();
            }
        }

        public sealed class PaymentsClient
        {
        }

        public sealed class PaymentJob(PaymentsClient paymentsClient)
        {
        }

        public sealed class Auditor
        {
        }

        public interface IServiceCollection
        {
        }

        public static class ServiceCollectionExtensions
        {
            public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
            public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;
            public static IServiceCollection AddScoped<TService>(this IServiceCollection services) => services;
        }
        """;

    [Fact]
    public async Task CodeFix_ChangesSingletonRegistrationToScoped()
    {
        var fixedSource = await CodeFixVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer, HCR004_ChangeToScopedLifetimeCodeFixProvider>
            .ApplyFirstCodeFixAsync(Fixture);

        Assert.Contains("services.AddScoped<PaymentJob>();", fixedSource, System.StringComparison.Ordinal);
        Assert.DoesNotContain("services.AddSingleton<PaymentJob>();", fixedSource, System.StringComparison.Ordinal);
        Assert.Contains("services.AddScoped<Auditor>();", fixedSource, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_FixAllChangesEveryFlaggedSingletonRegistration()
    {
        const string source = """
            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHttpClient<PaymentsClient>();
                    services.AddSingleton<PaymentJob>();
                    services.AddSingleton<PaymentReport>();
                }
            }

            public sealed class PaymentsClient
            {
            }

            public sealed class PaymentJob(PaymentsClient paymentsClient)
            {
            }

            public sealed class PaymentReport(PaymentsClient paymentsClient)
            {
            }

            public interface IServiceCollection
            {
            }

            public static class ServiceCollectionExtensions
            {
                public static IServiceCollection AddHttpClient<TClient>(this IServiceCollection services) => services;
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services) => services;
                public static IServiceCollection AddScoped<TService>(this IServiceCollection services) => services;
            }
            """;

        var fixedSource = await CodeFixVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer, HCR004_ChangeToScopedLifetimeCodeFixProvider>
            .ApplyFixAllInDocumentAsync(source);

        Assert.Empty(await AnalyzerVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer>.GetDiagnosticsAsync(fixedSource));
        Assert.DoesNotContain("services.AddSingleton<", fixedSource, System.StringComparison.Ordinal);
        Assert.Contains("services.AddScoped<PaymentJob>();", fixedSource, System.StringComparison.Ordinal);
        Assert.Contains("services.AddScoped<PaymentReport>();", fixedSource, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeFix_TitleMentionsScopedLifetime()
    {
        var titles = await CodeFixVerifier<HCR004_TypedClientInjectedIntoSingletonAnalyzer, HCR004_ChangeToScopedLifetimeCodeFixProvider>
            .GetCodeFixTitlesAsync(Fixture);

        Assert.Equal(HCR004_ChangeToScopedLifetimeCodeFixProvider.Title, Assert.Single(titles));
    }
}
