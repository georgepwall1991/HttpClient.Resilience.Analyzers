namespace HttpClient.Resilience.Analyzers.Models;

internal enum ServiceRegistrationKind
{
    HttpClient,
    Singleton,
    Scoped,
    Transient
}
