; Unshipped analyzer release

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
HCR006 | HttpClient.Lifetime | Warning | HttpClient.Timeout must be a positive TimeSpan
HCR021 | HttpClient.Handlers | Warning | DelegatingHandler.SendAsync should forward the cancellation token
HCR022 | HttpClient.Handlers | Warning | Do not disable server certificate validation
HCR065 | HttpClient.ResponseLifetime | Warning | Do not resend the same HttpRequestMessage instance
HCR088 | HttpClient.TypedClients | Warning | Do not send the same HttpRequestMessage more than once

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
