using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HttpClient.Resilience.Analyzers.Models;

/// <summary>
/// Shared recognition of the framework's IServiceCollection extension methods. Test stubs
/// live in the global namespace, so both the framework namespace and the global namespace
/// are accepted.
/// </summary>
internal static class FrameworkRegistrationMethods
{
    public static bool IsDependencyInjectionExtension(IMethodSymbol method)
    {
        // A reduced extension method keeps the namespace of its original declaration.
        var containingNamespace = method.ContainingNamespace;
        return containingNamespace.IsGlobalNamespace ||
            containingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";
    }

    public static bool IsFrameworkCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return IsDependencyInjectionExtension(method);
        }

        // Enumerable.All is true for an empty candidate set: unresolved calls remain conservative.
        return symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().All(IsDependencyInjectionExtension);
    }
}
