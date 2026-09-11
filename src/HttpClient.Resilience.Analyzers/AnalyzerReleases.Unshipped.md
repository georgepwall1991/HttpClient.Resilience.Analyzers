; Unshipped analyzer release

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
HCR021 | HttpClient.Handlers | Warning | DelegatingHandler.SendAsync should forward the cancellation token
HCR022 | HttpClient.Handlers | Warning | Do not disable server certificate validation
HCR065 | HttpClient.ResponseLifetime | Warning | Do not send the same HttpRequestMessage more than once

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
