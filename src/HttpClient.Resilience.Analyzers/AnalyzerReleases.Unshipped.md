; Unshipped analyzer release

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
HCR001 | HttpClient.Lifetime | Warning | Do not create and dispose HttpClient per request
HCR002 | HttpClient.Lifetime | Warning | Long-lived manual HttpClient should configure PooledConnectionLifetime
HCR003 | HttpClient.Lifetime | Warning | Do not cache IHttpClientFactory.CreateClient() results long-term
HCR004 | HttpClient.TypedClients | Warning | Do not inject typed HttpClient clients into singleton services
HCR005 | HttpClient.TypedClients | Warning | Do not separately register a typed client already registered by AddHttpClient<T>()
HCR020 | HttpClient.Handlers | Warning | DelegatingHandler should not capture scoped request data
HCR040 | HttpClient.Resilience | Warning | Do not stack duplicate resilience handlers
HCR041 | HttpClient.Resilience | Warning | Unsafe HTTP methods should not be retried unless explicitly configured
HCR060 | HttpClient.ResponseLifetime | Warning | Dispose HttpResponseMessage when using ResponseHeadersRead
HCR061 | HttpClient.ResponseLifetime | Warning | Check HTTP response success before reading content
HCR062 | HttpClient.ResponseLifetime | Warning | Prefer per-request headers over mutating DefaultRequestHeaders
HCR063 | HttpClient.ResponseLifetime | Warning | Avoid sync-over-async around outbound HTTP
HCR064 | HttpClient.ResponseLifetime | Warning | Use cancellation-aware HTTP APIs when a token is available
HCR080 | HttpClient.Concurrency | Info | High-concurrency HTTP fan-out should use bounded concurrency or connection limits
HCR081 | HttpClient.ResponseLifetime | Warning | Dispose streams returned from HTTP content
HCR082 | HttpClient.Resilience | Warning | Avoid per-request creation of resilience pipelines

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
