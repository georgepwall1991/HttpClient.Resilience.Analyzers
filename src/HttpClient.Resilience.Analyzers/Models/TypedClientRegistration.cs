namespace HttpClient.Resilience.Analyzers.Models;

internal sealed class TypedClientRegistration
{
    public TypedClientRegistration(string rawTypeName, string? resolvedTypeName)
    {
        RawTypeName = rawTypeName;
        ResolvedTypeName = resolvedTypeName;
    }

    public string RawTypeName { get; }

    public string? ResolvedTypeName { get; }
}
