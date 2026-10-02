using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HttpClient.Resilience.Analyzers.Models;

internal static class SyntaxTransparency
{
    public static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax postfix when
                    postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = postfix.Operand;
                    continue;
                default:
                    return expression;
            }
        }
    }

    public static bool LocalIsReassignedBetween(
        SyntaxNode containingNode,
        string localName,
        int start,
        int end)
    {
        return containingNode
            .DescendantNodes()
            .Any(node => node switch
            {
                AssignmentExpressionSyntax assignment =>
                    assignment.SpanStart > start &&
                    assignment.SpanStart < end &&
                    assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                    assignment.Left is IdentifierNameSyntax assignedIdentifier &&
                    assignedIdentifier.Identifier.ValueText == localName,
                // ref/out arguments reassign the variable just like an assignment.
                ArgumentSyntax argument =>
                    argument.SpanStart > start &&
                    argument.SpanStart < end &&
                    (argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) ||
                        argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)) &&
                    argument.Expression is IdentifierNameSyntax argumentIdentifier &&
                    argumentIdentifier.Identifier.ValueText == localName,
                _ => false
            });
    }
}
