using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HttpClient.Resilience.Analyzers.Models;

internal static class ResilienceHandlerInvocation
{
    public static bool IsFrameworkHandler(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        string methodName)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: var name
            } ||
            name != methodName)
        {
            return false;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return IsFrameworkResilienceExtension(method);
        }

        var candidateMethods = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
        // Stryker disable once logical: Length == 0 is vacuous All() true, so || and && are equivalent
        return candidateMethods.Length == 0 || candidateMethods.All(IsFrameworkResilienceExtension);
    }

    public static bool IsFrameworkResilienceExtension(IMethodSymbol method)
    {
        // Stryker disable once all: reduced extension methods keep the original namespace
        var containingNamespace = (method.ReducedFrom ?? method).ContainingNamespace;
        return containingNamespace.IsGlobalNamespace ||
            containingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";
    }
}
