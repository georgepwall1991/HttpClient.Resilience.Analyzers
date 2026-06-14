using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HttpClient.Resilience.Analyzers.Models;

internal sealed class ServiceRegistrationModel
{
    public ServiceRegistrationModel(
        ServiceRegistrationKind kind,
        string serviceTypeName,
        string? implementationTypeName,
        Location location,
        InvocationExpressionSyntax invocation)
    {
        Kind = kind;
        ServiceTypeName = serviceTypeName;
        ImplementationTypeName = implementationTypeName;
        Location = location;
        Invocation = invocation;
    }

    public ServiceRegistrationKind Kind { get; }

    public string ServiceTypeName { get; }

    public string? ImplementationTypeName { get; }

    public Location Location { get; }

    public InvocationExpressionSyntax Invocation { get; }

    public bool MatchesAnyType(ISet<string> typeNames)
    {
        return TypeNameUtilities.GetComparableNames(ServiceTypeName).Any(typeNames.Contains) ||
            (ImplementationTypeName is not null &&
                TypeNameUtilities.GetComparableNames(ImplementationTypeName).Any(typeNames.Contains));
    }
}
