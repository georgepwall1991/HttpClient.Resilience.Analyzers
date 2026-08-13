using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HttpClient.Resilience.Analyzers.Models;

internal static class ResilienceRetryInvocation
{
    public static ImmutableArray<InvocationExpressionSyntax> FindBuilderBoundAddRetries(
        InvocationExpressionSyntax addResilienceHandler,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (!TryGetConfigureCallback(
                addResilienceHandler,
                semanticModel,
                cancellationToken,
                out var searchRoot,
                out var builderParameter))
        {
            return ImmutableArray<InvocationExpressionSyntax>.Empty;
        }

        return searchRoot
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => IsBuilderBoundFrameworkAddRetry(
                invocation,
                builderParameter,
                semanticModel,
                cancellationToken))
            .ToImmutableArray();
    }

    public static bool IsFrameworkAddRetry(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (!TryGetInvokedMethodName(invocation, out var methodName) || methodName != "AddRetry")
        {
            return false;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return IsPollyOrGlobalExtension(method);
        }

        var candidateMethods = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
        // Stryker disable once logical: Length == 0 is vacuous All() true, so || and && are equivalent
        return candidateMethods.Length == 0 || candidateMethods.All(IsPollyOrGlobalExtension);
    }

    private static bool IsBuilderBoundFrameworkAddRetry(
        InvocationExpressionSyntax invocation,
        BuilderParameter builderParameter,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return IsFrameworkAddRetry(invocation, semanticModel, cancellationToken) &&
            ReceiverIsBuilderParameter(invocation, builderParameter, semanticModel, cancellationToken);
    }

    private static bool TryGetConfigureCallback(
        InvocationExpressionSyntax addResilienceHandler,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out SyntaxNode searchRoot,
        out BuilderParameter builderParameter)
    {
        foreach (var argument in addResilienceHandler.ArgumentList.Arguments)
        {
            if (TryGetCallback(
                    argument.Expression,
                    addResilienceHandler,
                    semanticModel,
                    cancellationToken,
                    out searchRoot,
                    out builderParameter))
            {
                return true;
            }
        }

        searchRoot = null!;
        builderParameter = default;
        return false;
    }

    private static bool TryGetCallback(
        ExpressionSyntax expression,
        InvocationExpressionSyntax addResilienceHandler,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out SyntaxNode searchRoot,
        out BuilderParameter builderParameter)
    {
        expression = SyntaxTransparency.Unwrap(expression);

        switch (expression)
        {
            case ParenthesizedLambdaExpressionSyntax parenthesized:
                return TryGetLambdaCallback(parenthesized, semanticModel, out searchRoot, out builderParameter);
            case SimpleLambdaExpressionSyntax simple:
                return TryGetLambdaCallback(simple, semanticModel, out searchRoot, out builderParameter);
            case AnonymousMethodExpressionSyntax anonymous when anonymous.ParameterList?.Parameters.Count > 0:
                searchRoot = GetAnonymousBody(anonymous);
                return TryGetBuilderParameter(
                    anonymous.ParameterList.Parameters[0],
                    semanticModel,
                    out builderParameter);
            case IdentifierNameSyntax identifier:
                return TryGetLocalFunctionCallback(
                    identifier,
                    addResilienceHandler,
                    semanticModel,
                    cancellationToken,
                    out searchRoot,
                    out builderParameter);
            default:
                searchRoot = null!;
                builderParameter = default;
                return false;
        }
    }

    private static bool TryGetLambdaCallback(
        LambdaExpressionSyntax lambda,
        SemanticModel semanticModel,
        out SyntaxNode searchRoot,
        out BuilderParameter builderParameter)
    {
        searchRoot = lambda.Body;
        var parameter = GetFirstLambdaParameter(lambda);
        if (parameter is null)
        {
            builderParameter = default;
            return false;
        }

        return TryGetBuilderParameter(parameter, semanticModel, out builderParameter);
    }

    private static ParameterSyntax? GetFirstLambdaParameter(LambdaExpressionSyntax lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter,
            ParenthesizedLambdaExpressionSyntax parenthesized
                when parenthesized.ParameterList.Parameters.Count > 0 =>
                parenthesized.ParameterList.Parameters[0],
            _ => null
        };
    }

    private static SyntaxNode GetAnonymousBody(AnonymousMethodExpressionSyntax anonymous)
    {
        return (SyntaxNode?)anonymous.Body ?? anonymous;
    }

    private static bool TryGetLocalFunctionCallback(
        IdentifierNameSyntax identifier,
        InvocationExpressionSyntax addResilienceHandler,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out SyntaxNode searchRoot,
        out BuilderParameter builderParameter)
    {
        searchRoot = null!;
        builderParameter = default;

        if (semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        var containingMethod = addResilienceHandler.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        foreach (var syntaxReference in method.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax(cancellationToken) is not LocalFunctionStatementSyntax localFunction ||
                localFunction.ParameterList.Parameters.Count == 0 ||
                localFunction.FirstAncestorOrSelf<MethodDeclarationSyntax>() != containingMethod)
            {
                continue;
            }

            var body = (SyntaxNode?)localFunction.Body ?? localFunction.ExpressionBody?.Expression;
            if (body is null ||
                !TryGetBuilderParameter(localFunction.ParameterList.Parameters[0], semanticModel, out builderParameter))
            {
                continue;
            }

            searchRoot = body;
            return true;
        }

        return false;
    }

    private static bool TryGetBuilderParameter(
        ParameterSyntax parameter,
        SemanticModel semanticModel,
        out BuilderParameter builderParameter)
    {
        builderParameter = new BuilderParameter(
            semanticModel.GetDeclaredSymbol(parameter),
            parameter.Identifier.ValueText);
        return true;
    }

    private static bool ReceiverIsBuilderParameter(
        InvocationExpressionSyntax invocation,
        BuilderParameter builderParameter,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        var receiver = SyntaxTransparency.Unwrap(memberAccess.Expression);
        if (receiver is IdentifierNameSyntax identifier)
        {
            return IdentifierIsBuilderParameter(identifier, builderParameter, semanticModel, cancellationToken);
        }

        return receiver is InvocationExpressionSyntax receiverInvocation &&
            ReceiverIsBuilderParameter(receiverInvocation, builderParameter, semanticModel, cancellationToken);
    }

    private static bool IdentifierIsBuilderParameter(
        IdentifierNameSyntax identifier,
        BuilderParameter builderParameter,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (builderParameter.Symbol is { } parameterSymbol)
        {
            return SymbolEqualityComparer.Default.Equals(
                semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol,
                parameterSymbol);
        }

        return identifier.Identifier.ValueText == builderParameter.Name;
    }

    private static bool TryGetInvokedMethodName(InvocationExpressionSyntax invocation, out string methodName)
    {
        methodName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax { Name: GenericNameSyntax generic } => generic.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => string.Empty
        };

        return methodName.Length > 0;
    }

    private static bool IsPollyOrGlobalExtension(IMethodSymbol method)
    {
        var containingNamespace = (method.ReducedFrom ?? method).ContainingNamespace;
        return containingNamespace.IsGlobalNamespace ||
            containingNamespace.ToDisplayString() == "Polly";
    }

    private readonly struct BuilderParameter
    {
        public BuilderParameter(IParameterSymbol? symbol, string name)
        {
            Symbol = symbol;
            Name = name;
        }

        public IParameterSymbol? Symbol { get; }

        public string Name { get; }
    }
}
