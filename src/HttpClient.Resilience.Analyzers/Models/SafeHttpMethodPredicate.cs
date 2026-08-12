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
                Name.Identifier.ValueText: "Equals",
                Expression: MemberAccessExpressionSyntax httpMethodMember
            } &&
            IsFrameworkHttpMethodMember(httpMethodMember, semanticModel, cancellationToken) &&
            HttpMethodSafety.IsSafeHttpMethodName(httpMethodMember.Name.Identifier.ValueText))
        {
            return true;
        }

        if (invocation.ArgumentList.Arguments.Count != 2 ||
            !IsSystemObjectEqualsInvocation(invocation, semanticModel, cancellationToken))
        {
            return false;
        }

        var httpMethodMembers = invocation.ArgumentList.Arguments
            .SelectMany(argument => argument.Expression
                .DescendantNodesAndSelf()
                .OfType<MemberAccessExpressionSyntax>())
            .Where(memberAccess => IsFrameworkHttpMethodMember(memberAccess, semanticModel, cancellationToken))
            .Select(memberAccess => memberAccess.Name.Identifier.ValueText)
            .ToArray();

        return httpMethodMembers.Any(HttpMethodSafety.IsSafeHttpMethodName) &&
            !httpMethodMembers.Any(method => HttpMethodSafety.IsUnsafeHttpMethodName(method, ignoreCase: false));
    }

    private static bool IsSystemObjectEqualsInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return IsSystemObjectEqualsMethod(method);
        }

        var candidateMethods = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
        return candidateMethods.Length > 0 && candidateMethods.All(IsSystemObjectEqualsMethod);
    }

    private static bool IsSystemObjectEqualsMethod(IMethodSymbol method)
    {
        return method.Name == "Equals" &&
            (method.ReducedFrom ?? method).ContainingType.SpecialType == SpecialType.System_Object;
    }

    private static bool IsSafeHttpMethodEquality(
        BinaryExpressionSyntax binary,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var httpMethodMembers = binary
            .ChildNodes()
            .OfType<ExpressionSyntax>()
            .Select(SyntaxTransparency.Unwrap)
            .SelectMany(operand => operand.DescendantNodesAndSelf())
            .OfType<MemberAccessExpressionSyntax>()
            .Where(memberAccess => IsFrameworkHttpMethodMember(memberAccess, semanticModel, cancellationToken))
            .Select(memberAccess => memberAccess.Name.Identifier.ValueText)
            .ToArray();

        return httpMethodMembers.Any(HttpMethodSafety.IsSafeHttpMethodName) &&
            !httpMethodMembers.Any(method => HttpMethodSafety.IsUnsafeHttpMethodName(method, ignoreCase: false));
    }

    private static bool IsFrameworkShouldHandleProperty(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        string expectedNamespace)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(memberAccess, cancellationToken);
        if (symbolInfo.Symbol is ISymbol symbol)
        {
            return IsFrameworkShouldHandleProperty(symbol, expectedNamespace);
        }

        return symbolInfo.CandidateSymbols.Length == 0 ||
            symbolInfo.CandidateSymbols.All(candidate => IsFrameworkShouldHandleProperty(candidate, expectedNamespace));
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
        if (symbolInfo.Symbol is ISymbol symbol)
        {
            return IsFrameworkHttpMethodMember(symbol);
        }

        return symbolInfo.CandidateSymbols.Length == 0 ||
            symbolInfo.CandidateSymbols.All(IsFrameworkHttpMethodMember);
    }

    private static bool IsFrameworkHttpMethodMember(ISymbol symbol)
    {
        return symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::System.Net.Http.HttpMethod";
    }
}
