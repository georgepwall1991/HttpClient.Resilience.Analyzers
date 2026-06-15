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
        "Do not stack duplicate resilience handlers",
        "Do not stack duplicate resilience handlers",
        DiagnosticCategories.Resilience);

    public static readonly DiagnosticDescriptor HCR041 = Create(
        DiagnosticIds.HCR041,
        "Unsafe HTTP methods should not be retried unless explicitly configured",
        "Standard resilience retries unsafe HTTP methods. Disable retries for POST/PUT/PATCH/DELETE/CONNECT unless the operation is idempotent.",
        DiagnosticCategories.Resilience);

    public static readonly DiagnosticDescriptor HCR060 = Create(
        DiagnosticIds.HCR060,
        "Dispose HttpResponseMessage when using ResponseHeadersRead",
        "Dispose HttpResponseMessage when using ResponseHeadersRead",
        DiagnosticCategories.ResponseLifetime);

    public static readonly DiagnosticDescriptor HCR061 = Create(
        DiagnosticIds.HCR061,
        "Check HTTP response success before reading content",
        "Check HTTP response success before reading content",
        DiagnosticCategories.ResponseLifetime);

    public static readonly DiagnosticDescriptor HCR062 = Create(
        DiagnosticIds.HCR062,
        "Prefer per-request headers over mutating DefaultRequestHeaders",
        "Prefer per-request headers over mutating DefaultRequestHeaders",
        DiagnosticCategories.ResponseLifetime);

    public static readonly DiagnosticDescriptor HCR063 = Create(
        DiagnosticIds.HCR063,
        "Avoid sync-over-async around outbound HTTP",
        "Avoid sync-over-async around outbound HTTP",
        DiagnosticCategories.ResponseLifetime);

    public static readonly DiagnosticDescriptor HCR064 = Create(
        DiagnosticIds.HCR064,
        "Use cancellation-aware HTTP APIs when a token is available",
        "Use cancellation-aware HTTP APIs when a token is available",
        DiagnosticCategories.ResponseLifetime);

    public static readonly DiagnosticDescriptor HCR080 = Create(
        DiagnosticIds.HCR080,
        "High-concurrency HTTP fan-out should use bounded concurrency or connection limits",
        "High-concurrency HTTP fan-out should use bounded concurrency or connection limits",
        DiagnosticCategories.Concurrency,
        DiagnosticSeverity.Info);

    public static readonly DiagnosticDescriptor HCR081 = Create(
        DiagnosticIds.HCR081,
        "Dispose streams returned from HTTP content",
        "Dispose streams returned from HTTP content",
        DiagnosticCategories.ResponseLifetime);

    public static readonly DiagnosticDescriptor HCR082 = Create(
        DiagnosticIds.HCR082,
        "Avoid per-request creation of resilience pipelines",
        "Avoid per-request creation of resilience pipelines",
        DiagnosticCategories.Resilience);

    public static readonly DiagnosticDescriptor HCR083 = Create(
        DiagnosticIds.HCR083,
        "Configure BaseAddress for typed clients that use relative URLs",
        "Configure BaseAddress for typed clients that use relative URLs",
        DiagnosticCategories.TypedClients);

    public static readonly DiagnosticDescriptor HCR084 = Create(
        DiagnosticIds.HCR084,
        "Avoid duplicated string literals for named HttpClient names",
        "Avoid duplicated string literals for named HttpClient names",
        DiagnosticCategories.TypedClients);

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
            helpLinkUri: $"https://github.com/georgepwall1991/HttpClient.Resilience.Analyzers/blob/main/docs/rules/{id}.md");
    }
}
