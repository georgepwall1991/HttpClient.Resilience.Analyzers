using System.Net.Http;

public sealed class BadPaymentsService
{
    public async Task<string> CreateAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        using var response = await client.GetAsync("https://example.com", cancellationToken);

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}

public sealed class BadStaticClient
{
    private static readonly HttpClient Client = new();

    public Task<HttpResponseMessage> GetAsync(CancellationToken cancellationToken)
    {
        return Client.GetAsync("https://example.com", cancellationToken);
    }
}

public sealed class BadFactoryClientCache
{
    private static HttpClient _client = null!;

    public static void Initialize(IHttpClientFactory factory)
    {
        _client = factory.CreateClient("github");
    }
}

public static class BadResilienceRegistration
{
    public static IHttpClientBuilder Configure(IServiceCollection services)
    {
        return services
            .AddHttpClient<BadPaymentsService>()
            .AddStandardResilienceHandler()
            .AddStandardResilienceHandler();
    }
}

public static class BadTypedClientLifetimeRegistration
{
    public static void Configure(IServiceCollection services)
    {
        services.AddHttpClient<BadPaymentsService>();
        services.AddSingleton<BadPaymentJob>();
    }
}

public static class BadDuplicateTypedClientRegistration
{
    public static void Configure(IServiceCollection services)
    {
        services.AddHttpClient<BadPaymentsService>();
        services.AddTransient<BadPaymentsService>();
    }
}

public static class BadUnsafeRetryRegistration
{
    public static IHttpClientBuilder Configure(IServiceCollection services)
    {
        return services
            .AddHttpClient<BadUnsafePaymentsClient>()
            .AddStandardResilienceHandler();
    }
}

public static class BadNamedUnsafeRetryRegistration
{
    public static IHttpClientBuilder Configure(IServiceCollection services)
    {
        return services
            .AddHttpClient("payments")
            .AddStandardResilienceHandler();
    }
}

public sealed class BadPaymentJob(BadPaymentsService paymentsService)
{
    public BadPaymentsService PaymentsService { get; } = paymentsService;
}

public sealed class BadUnsafePaymentsClient(HttpClient httpClient)
{
    public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
    {
        return httpClient.PostAsync("https://example.com/payments", null, cancellationToken);
    }
}

public sealed class BadNamedPaymentsJob(IHttpClientFactory factory)
{
    public Task<HttpResponseMessage> CreateAsync(CancellationToken cancellationToken)
    {
        var client = factory.CreateClient("payments");
        return client.PostAsync("https://example.com/payments", null, cancellationToken);
    }
}

public sealed class BadUserHeaderHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    public IHttpContextAccessor Accessor { get; } = accessor;
}

public sealed class BadFanOutService
{
    public Task SendAsync(HttpClient client, IEnumerable<string> urls, CancellationToken cancellationToken)
    {
        return Task.WhenAll(urls.Select(url => client.GetAsync(url, cancellationToken)));
    }
}

public sealed class BadUncheckedResponseService
{
    public async Task<string> ReadAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync("https://example.com", cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}

public interface IServiceCollection
{
}

public interface IHttpClientFactory
{
    HttpClient CreateClient(string name);
}

public interface IHttpContextAccessor
{
}

public interface IHttpClientBuilder
{
}

public static class HttpClientBuilderExtensions
{
    public static IHttpClientBuilder AddHttpClient<TClient>(this IServiceCollection services)
    {
        return new DemoHttpClientBuilder();
    }

    public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name)
    {
        return new DemoHttpClientBuilder();
    }

    public static IHttpClientBuilder AddStandardResilienceHandler(this IHttpClientBuilder builder)
    {
        return builder;
    }

    public static IServiceCollection AddSingleton<TService>(this IServiceCollection services)
    {
        return services;
    }

    public static IServiceCollection AddTransient<TService>(this IServiceCollection services)
    {
        return services;
    }

    private sealed class DemoHttpClientBuilder : IHttpClientBuilder
    {
    }
}
