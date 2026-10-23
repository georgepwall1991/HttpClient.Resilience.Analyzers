; Unshipped analyzer release

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
HCR006 | HttpClient.Lifetime | Warning | HttpClient.Timeout must be a positive TimeSpan
HCR021 | HttpClient.Handlers | Warning | DelegatingHandler.SendAsync should forward the cancellation token
HCR022 | HttpClient.Handlers | Warning | Do not disable server certificate validation
HCR065 | HttpClient.ResponseLifetime | Warning | Do not send the same HttpRequestMessage more than once
HCR087 | HttpClient.Lifetime | Warning | BaseAddress should end with a trailing slash
HCR088 | HttpClient.TypedClients | Warning | Typed client has no HttpClient-accepting constructor

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
