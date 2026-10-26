using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HttpClient.Resilience.Analyzers.Models;

/// <summary>
/// Detects types that implement the framework health-check contract. Health checks run on
/// probe requests, so clients created in their methods are short-lived even when the type
/// name does not look request-shaped.
/// </summary>
internal static class HealthCheckTypeContext
{
    private const string HealthCheckInterfaceMetadataName =
        "global::Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck";

    public static bool IsHealthCheckType(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (node.FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } type)
        {
            return false;
        }

        return semanticModel.GetDeclaredSymbol(type, cancellationToken) is { } typeSymbol &&
            typeSymbol.AllInterfaces.Any(candidate =>
                candidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                    HealthCheckInterfaceMetadataName);
    }
}
