using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HttpClient.Resilience.Analyzers.Models;

internal static class SafeHttpMethodPredicate
{
    public static bool ContainsSafeOnlyShouldHandle(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        string expectedNamespace,
        string? requiredOwnerName)
    {
        return invocation
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => IsSafeOnlyShouldHandleAssignment(
                assignment,
                semanticModel,
                cancellationToken,
                expectedNamespace,
                requiredOwnerName));
    }

    private static bool IsSafeOnlyShouldHandleAssignment(
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        string expectedNamespace,
        string? requiredOwnerName)
    {
        if (assignment.Left is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "ShouldHandle"
            } shouldHandleMember ||
            !IsFrameworkShouldHandleProperty(
                shouldHandleMember,
                semanticModel,
                cancellationToken,
                expectedNamespace) ||
            !OwnerMatches(shouldHandleMember, semanticModel, cancellationToken, expectedNamespace, requiredOwnerName))
        {
            return false;
        }

        var predicateExpression = GetPredicateExpression(assignment.Right);

        return predicateExpression is not null &&
            IsSafeOnlyPredicateExpression(predicateExpression, semanticModel, cancellationToken);
    }

    private static bool OwnerMatches(
        MemberAccessExpressionSyntax shouldHandleMember,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        string expectedNamespace,
        string? requiredOwnerName)
    {
        if (requiredOwnerName is null)
        {
            return true;
        }

        if (shouldHandleMember.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: var ownerName
            } &&
            ownerName == requiredOwnerName)
        {
            return true;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(shouldHandleMember, cancellationToken);
        if (symbolInfo.Symbol is IPropertySymbol property)
        {
            return property.ContainingNamespace.ToDisplayString() == expectedNamespace;
        }

        return false;
    }

    private static ExpressionSyntax? GetPredicateExpression(ExpressionSyntax expression)
    {
        if (expression is not LambdaExpressionSyntax lambda)
        {
            return null;
        }

        if (lambda.Body is ExpressionSyntax expressionBody)
        {
            return expressionBody;
        }

        return lambda.Body is BlockSyntax { Statements.Count: 1 } block &&
            block.Statements[0] is ReturnStatementSyntax { Expression: { } returnExpression }
                ? returnExpression
                : null;
    }

    private static bool IsSafeOnlyPredicateExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => IsSafeOnlyPredicateExpression(
                parenthesized.Expression,
                semanticModel,
                cancellationToken),
            PostfixUnaryExpressionSyntax postfix when
                postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) =>
                IsSafeOnlyPredicateExpression(postfix.Operand, semanticModel, cancellationToken),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalOrExpression) =>
                IsSafeOnlyPredicateExpression(binary.Left, semanticModel, cancellationToken) &&
                IsSafeOnlyPredicateExpression(binary.Right, semanticModel, cancellationToken),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression) =>
                IsSafeOnlyPredicateExpression(binary.Left, semanticModel, cancellationToken) ||
                IsSafeOnlyPredicateExpression(binary.Right, semanticModel, cancellationToken),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) =>
                IsSafeHttpMethodEquality(binary, semanticModel, cancellationToken),
            InvocationExpressionSyntax invocation =>
                IsSafeHttpMethodEqualsInvocation(invocation, semanticModel, cancellationToken),
            _ => false
        };
    }

    private static bool IsSafeHttpMethodEqualsInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (invocation.ArgumentList.Arguments.Count == 1 &&
            invocation.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Equals"
            } equalsMember)
        {
            return IsSafeHttpMethodComparedToRequestMethod(
                equalsMember.Expression,
                invocation.ArgumentList.Arguments[0].Expression,
                semanticModel,
                cancellationToken);
        }

        if (invocation.ArgumentList.Arguments.Count != 2 ||
            !IsSystemObjectEqualsInvocation(invocation, semanticModel, cancellationToken))
        {
            return false;
        }

        var left = invocation.ArgumentList.Arguments[0].Expression;
        var right = invocation.ArgumentList.Arguments[1].Expression;
        return IsSafeHttpMethodComparedToRequestMethod(left, right, semanticModel, cancellationToken) ||
            IsSafeHttpMethodComparedToRequestMethod(right, left, semanticModel, cancellationToken);
    }

    private static bool IsSystemObjectEqualsInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        return symbolInfo.Symbol is IMethodSymbol method && IsSystemObjectEqualsMethod(method);
    }

    private static bool IsSystemObjectEqualsMethod(IMethodSymbol method)
    {
        return method.Name == "Equals" &&
            // Stryker disable once all: reduced object.Equals keeps System.Object as containing type
            (method.ReducedFrom ?? method).ContainingType.SpecialType == SpecialType.System_Object;
    }

    private static bool IsSafeHttpMethodEquality(
        BinaryExpressionSyntax binary,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var left = SyntaxTransparency.Unwrap(binary.Left);
        var right = SyntaxTransparency.Unwrap(binary.Right);
        return IsSafeHttpMethodComparedToRequestMethod(left, right, semanticModel, cancellationToken) ||
            IsSafeHttpMethodComparedToRequestMethod(right, left, semanticModel, cancellationToken);
    }

    /// <summary>
    /// True only when a safe <c>HttpMethod</c> constant is compared with something that is
    /// not itself an <c>HttpMethod.*</c> constant. <c>HttpMethod.Get == HttpMethod.Get</c>
    /// always returns true and must not suppress unsafe-method diagnostics.
    /// </summary>
    private static bool IsSafeHttpMethodComparedToRequestMethod(
        ExpressionSyntax httpMethodSide,
        ExpressionSyntax requestSide,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return TryGetFrameworkHttpMethodConstantName(
                httpMethodSide,
                semanticModel,
                cancellationToken,
                out var methodName) &&
            HttpMethodSafety.IsSafeHttpMethodName(methodName) &&
            !TryGetFrameworkHttpMethodConstantName(
                requestSide,
                semanticModel,
                cancellationToken,
                out _);
    }

    private static bool TryGetFrameworkHttpMethodConstantName(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out string methodName)
    {
        // Stryker disable once string: out-param default is unused on the false path
        methodName = string.Empty;
        if (SyntaxTransparency.Unwrap(expression) is not MemberAccessExpressionSyntax memberAccess ||
            !IsFrameworkHttpMethodMember(memberAccess, semanticModel, cancellationToken))
        {
            return false;
        }

        methodName = memberAccess.Name.Identifier.ValueText;
        return true;
    }

    private static bool IsFrameworkShouldHandleProperty(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        string expectedNamespace)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(memberAccess, cancellationToken);
        return symbolInfo.Symbol is ISymbol symbol &&
            IsFrameworkShouldHandleProperty(symbol, expectedNamespace);
    }

    private static bool IsFrameworkShouldHandleProperty(ISymbol symbol, string expectedNamespace)
    {
        return symbol is IPropertySymbol property &&
            (property.ContainingNamespace.IsGlobalNamespace ||
                property.ContainingNamespace.ToDisplayString() == expectedNamespace);
    }

    private static bool IsFrameworkHttpMethodMember(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(memberAccess, cancellationToken);
        return symbolInfo.Symbol is ISymbol symbol && IsFrameworkHttpMethodMember(symbol);
    }

    private static bool IsFrameworkHttpMethodMember(ISymbol symbol)
    {
        return symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::System.Net.Http.HttpMethod";
    }
}
