using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HttpClient.Resilience.Analyzers.Models;

internal static class RetryUnsafeMethodGuard
{
    public static bool HasVisibleGuard(
        InvocationExpressionSyntax addRetry,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return ContainsDisableForUnsafeHttpMethods(addRetry, semanticModel, cancellationToken) ||
            SafeHttpMethodPredicate.ContainsSafeOnlyShouldHandle(
                addRetry,
                semanticModel,
                cancellationToken,
                expectedNamespace: "Polly.Retry",
                requiredOwnerName: null) ||
            HasSafeShouldHandleOnOptionsLocal(addRetry, semanticModel, cancellationToken) ||
            ContainsLiteralZeroMaxRetryAttempts(addRetry, semanticModel, cancellationToken);
    }

    public static bool ContainsDisableForUnsafeHttpMethods(
        InvocationExpressionSyntax searchRoot,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return searchRoot
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(child => IsDisableForUnsafeHttpMethodsInvocation(child, semanticModel, cancellationToken)) ||
            HasDisableForUnsafeHttpMethodsOnOptionsLocal(searchRoot, semanticModel, cancellationToken);
    }

    private static bool IsDisableForUnsafeHttpMethodsInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "DisableForUnsafeHttpMethods"
            })
        {
            return false;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return IsFrameworkUnsafeMethodRetryGuard(method);
        }

        var candidateMethods = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
        return candidateMethods.Length == 0 || candidateMethods.All(IsFrameworkUnsafeMethodRetryGuard);
    }

    private static bool IsFrameworkUnsafeMethodRetryGuard(IMethodSymbol method)
    {
        var containingNamespace = (method.ReducedFrom ?? method).ContainingNamespace;
        return containingNamespace.IsGlobalNamespace ||
            containingNamespace.ToDisplayString() == "Microsoft.Extensions.Http.Resilience";
    }

    private static bool HasDisableForUnsafeHttpMethodsOnOptionsLocal(
        InvocationExpressionSyntax addRetry,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (!TryGetOptionsLocal(addRetry, semanticModel, cancellationToken, out var optionsLocal, out var searchRoot))
        {
            return false;
        }

        return searchRoot
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation =>
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                IdentifierRefersToLocal(memberAccess.Expression, optionsLocal, semanticModel, cancellationToken) &&
                IsDisableForUnsafeHttpMethodsInvocation(invocation, semanticModel, cancellationToken));
    }

    private static bool HasSafeShouldHandleOnOptionsLocal(
        InvocationExpressionSyntax addRetry,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (!TryGetOptionsLocal(addRetry, semanticModel, cancellationToken, out var optionsLocal, out var searchRoot))
        {
            return false;
        }

        if (optionsLocal.DeclaringSyntaxReferences.Length > 0)
        {
            foreach (var syntaxReference in optionsLocal.DeclaringSyntaxReferences)
            {
                if (syntaxReference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax declarator &&
                    declarator.Initializer?.Value is { } initializer &&
                    HasSafeShouldHandleAssignmentInTree(initializer, semanticModel, cancellationToken))
                {
                    return true;
                }
            }
        }

        return searchRoot
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment =>
                assignment.Left is MemberAccessExpressionSyntax memberAccess &&
                IdentifierRefersToLocal(memberAccess.Expression, optionsLocal, semanticModel, cancellationToken) &&
                SafeHttpMethodPredicate.IsSafeOnlyShouldHandleAssignment(
                    assignment,
                    semanticModel,
                    cancellationToken,
                    expectedNamespace: "Polly.Retry",
                    requiredOwnerName: null));
    }

    private static bool HasSafeShouldHandleAssignmentInTree(
        SyntaxNode searchRoot,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return searchRoot
            .DescendantNodesAndSelf()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => SafeHttpMethodPredicate.IsSafeOnlyShouldHandleAssignment(
                assignment,
                semanticModel,
                cancellationToken,
                expectedNamespace: "Polly.Retry",
                requiredOwnerName: null));
    }

    private static bool ContainsLiteralZeroMaxRetryAttempts(
        InvocationExpressionSyntax addRetry,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (HasLiteralZeroMaxRetryAttemptsInTree(addRetry, semanticModel, cancellationToken))
        {
            return true;
        }

        if (!TryGetOptionsLocal(addRetry, semanticModel, cancellationToken, out var optionsLocal, out var searchRoot))
        {
            return false;
        }

        if (optionsLocal.DeclaringSyntaxReferences.Length > 0)
        {
            foreach (var syntaxReference in optionsLocal.DeclaringSyntaxReferences)
            {
                if (syntaxReference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax declarator &&
                    declarator.Initializer?.Value is { } initializer &&
                    HasLiteralZeroMaxRetryAttemptsInTree(initializer, semanticModel, cancellationToken))
                {
                    return true;
                }
            }
        }

        return searchRoot
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment =>
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                assignment.Left is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.ValueText == "MaxRetryAttempts" &&
                IdentifierRefersToLocal(memberAccess.Expression, optionsLocal, semanticModel, cancellationToken) &&
                IsIntegerZero(assignment.Right, semanticModel, cancellationToken));
    }

    private static bool HasLiteralZeroMaxRetryAttemptsInTree(
        SyntaxNode searchRoot,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return searchRoot
            .DescendantNodesAndSelf()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment =>
                assignment.Left is IdentifierNameSyntax
                {
                    Identifier.ValueText: "MaxRetryAttempts"
                } &&
                IsIntegerZero(assignment.Right, semanticModel, cancellationToken));
    }

    private static bool IsIntegerZero(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var constant = semanticModel.GetConstantValue(expression, cancellationToken);
        return constant.HasValue && constant.Value is 0;
    }

    private static bool TryGetOptionsLocal(
        InvocationExpressionSyntax addRetry,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out ILocalSymbol optionsLocal,
        out SyntaxNode searchRoot)
    {
        optionsLocal = null!;
        searchRoot = null!;

        if (addRetry.ArgumentList.Arguments.Count == 0)
        {
            return false;
        }

        var argument = SyntaxTransparency.Unwrap(addRetry.ArgumentList.Arguments[0].Expression);
        if (argument is not IdentifierNameSyntax identifier ||
            semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol is not ILocalSymbol local)
        {
            return false;
        }

        optionsLocal = local;
        searchRoot = (SyntaxNode?)addRetry.FirstAncestorOrSelf<LambdaExpressionSyntax>()?.Body ??
            (SyntaxNode?)addRetry.FirstAncestorOrSelf<LocalFunctionStatementSyntax>()?.Body ??
            (SyntaxNode?)addRetry.FirstAncestorOrSelf<MethodDeclarationSyntax>()?.Body ??
            addRetry;
        return true;
    }

    private static bool IdentifierRefersToLocal(
        ExpressionSyntax expression,
        ILocalSymbol optionsLocal,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return SyntaxTransparency.Unwrap(expression) is IdentifierNameSyntax identifier &&
            SymbolEqualityComparer.Default.Equals(
                semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol,
                optionsLocal);
    }
}
