using Microsoft.CodeAnalysis;

namespace HttpClient.Resilience.Analyzers.Diagnostics;

public static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor HCR001 = Create(
        DiagnosticIds.HCR001,
        "Do not create and dispose HttpClient per request",
        "Do not create and dispose HttpClient per request",
        DiagnosticCategories.Lifetime);

    public static readonly DiagnosticDescriptor HCR002 = Create(
        DiagnosticIds.HCR002,
        "Long-lived manual HttpClient should configure PooledConnectionLifetime",
        "Long-lived manual HttpClient should configure PooledConnectionLifetime",
        DiagnosticCategories.Lifetime);

    public static readonly DiagnosticDescriptor HCR003 = Create(
        DiagnosticIds.HCR003,
        "Do not cache IHttpClientFactory.CreateClient() results long-term",
        "Do not cache IHttpClientFactory.CreateClient() results long-term",
        DiagnosticCategories.Lifetime);

    public static readonly DiagnosticDescriptor HCR004 = Create(
        DiagnosticIds.HCR004,
        "Do not inject typed HttpClient clients into singleton services",
        "Do not inject typed HttpClient clients into singleton services",
        DiagnosticCategories.TypedClients);

    public static readonly DiagnosticDescriptor HCR005 = Create(
        DiagnosticIds.HCR005,
        "Do not separately register a typed client already registered by AddHttpClient<T>()",
        "Do not separately register a typed client already registered by AddHttpClient<T>()",
        DiagnosticCategories.TypedClients);

    public static readonly DiagnosticDescriptor HCR020 = Create(
        DiagnosticIds.HCR020,
        "DelegatingHandler should not capture scoped request data",
        "DelegatingHandler should not capture scoped request data",
        DiagnosticCategories.Handlers);

    public static readonly DiagnosticDescriptor HCR040 = Create(
        DiagnosticIds.HCR040,
        "Do not stack multiple standard resilience handlers",
        "Do not stack multiple standard resilience handlers",
        DiagnosticCategories.Resilience);

    public static readonly DiagnosticDescriptor HCR041 = Create(
        DiagnosticIds.HCR041,
        "Unsafe HTTP methods should not be retried unless explicitly configured",
        "Standard resilience retries unsafe HTTP methods. Disable retries for POST/PUT/PATCH/DELETE unless the operation is idempotent.",
        DiagnosticCategories.Resilience);

    public static readonly DiagnosticDescriptor HCR060 = Create(
        DiagnosticIds.HCR060,
        "Dispose HttpResponseMessage when using ResponseHeadersRead",
        "Dispose HttpResponseMessage when using ResponseHeadersRead",
        DiagnosticCategories.ResponseLifetime);

    public static readonly DiagnosticDescriptor HCR080 = Create(
        DiagnosticIds.HCR080,
        "High-concurrency HTTP fan-out should use bounded concurrency or connection limits",
        "High-concurrency HTTP fan-out should use bounded concurrency or connection limits",
        DiagnosticCategories.Concurrency,
        DiagnosticSeverity.Info);

    private static DiagnosticDescriptor Create(
        string id,
        string title,
        string messageFormat,
        string category,
        DiagnosticSeverity defaultSeverity = DiagnosticSeverity.Warning)
    {
        return new DiagnosticDescriptor(
            id,
            title,
            messageFormat,
            category,
            defaultSeverity,
            isEnabledByDefault: true,
            helpLinkUri: $"https://github.com/georg-jung/HttpClient.Resilience.Analyzers/blob/main/docs/rules/{id}.md");
    }
}
