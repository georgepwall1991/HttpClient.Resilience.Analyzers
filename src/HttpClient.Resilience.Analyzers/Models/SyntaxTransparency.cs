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

    /// <summary>
    /// True when two nodes cannot both execute: opposite branches of the same if/else,
    /// code after an if whose branch always exits, opposite arms of a conditional
    /// expression, or different sections of the same switch.
    /// </summary>
    public static bool AreInMutuallyExclusiveBranches(SyntaxNode first, SyntaxNode second)
    {
        for (var ancestor = first.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is IfStatementSyntax ifStatement)
            {
                var firstInThen = ifStatement.Statement.Span.Contains(first.Span);
                var secondInThen = ifStatement.Statement.Span.Contains(second.Span);

                if (ifStatement.Else is { } elseClause)
                {
                    var firstInElse = elseClause.Span.Contains(first.Span);
                    var secondInElse = elseClause.Span.Contains(second.Span);
                    if ((firstInThen && secondInElse) || (firstInElse && secondInThen))
                    {
                        return true;
                    }
                }
                else if (firstInThen &&
                    second.SpanStart >= ifStatement.Span.End &&
                    StatementAlwaysExits(ifStatement.Statement))
                {
                    // The guard branch always returns or throws, so the code after the
                    // if statement only runs when the branch did not.
                    return true;
                }
            }
            else if (ancestor is ConditionalExpressionSyntax conditional)
            {
                var firstWhenTrue = conditional.WhenTrue.Span.Contains(first.Span);
                var secondWhenTrue = conditional.WhenTrue.Span.Contains(second.Span);
                var firstWhenFalse = conditional.WhenFalse.Span.Contains(first.Span);
                var secondWhenFalse = conditional.WhenFalse.Span.Contains(second.Span);

                if ((firstWhenTrue && secondWhenFalse) || (firstWhenFalse && secondWhenTrue))
                {
                    return true;
                }
            }
            else if (ancestor is SwitchSectionSyntax firstSection &&
                firstSection.Parent is SwitchStatementSyntax switchStatement)
            {
                var secondSection = second.FirstAncestorOrSelf<SwitchSectionSyntax>();
                if (secondSection is not null &&
                    !ReferenceEquals(firstSection, secondSection) &&
                    ReferenceEquals(secondSection.Parent, switchStatement))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// True when a statement unconditionally transfers control away from the current path:
    /// a throw, return, continue, or break, including as the last statement of a block.
    /// </summary>
    public static bool StatementAlwaysExits(StatementSyntax statement)
    {
        return statement switch
        {
            ThrowStatementSyntax or ReturnStatementSyntax or ContinueStatementSyntax or BreakStatementSyntax => true,
            BlockSyntax { Statements.Count: > 0 } block =>
                StatementAlwaysExits(block.Statements[block.Statements.Count - 1]),
            _ => false
        };
    }

    /// <summary>
    /// True when an assignment replaces a local with a new value that consumes the
    /// local's previous value, as in <c>stream = new Wrapper(stream)</c> or
    /// <c>stream = Transform(stream)</c>. The replacement still owns the old value,
    /// so this is a transfer rather than a lost reference.
    /// </summary>
    public static bool IsSelfWrappingAssignment(AssignmentExpressionSyntax assignment)
    {
        return assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
            assignment.Left is IdentifierNameSyntax identifier &&
            IsSelfWrappingExpression(assignment.Right, identifier.Identifier.ValueText);
    }

    private static bool IsSelfWrappingExpression(ExpressionSyntax expression, string variableName)
    {
        expression = Unwrap(expression);
        return expression switch
        {
            ObjectCreationExpressionSyntax creation =>
                creation.ArgumentList?.Arguments.Any(argument =>
                    ContainsIdentifier(argument.Expression, variableName)) == true,
            ImplicitObjectCreationExpressionSyntax creation =>
                creation.ArgumentList.Arguments.Any(argument =>
                    ContainsIdentifier(argument.Expression, variableName)),
            InvocationExpressionSyntax invocation =>
                invocation.ArgumentList.Arguments.Any(argument =>
                    ContainsIdentifier(argument.Expression, variableName)),
            CastExpressionSyntax cast => IsSelfWrappingExpression(cast.Expression, variableName),
            _ => false
        };
    }

    private static bool ContainsIdentifier(ExpressionSyntax expression, string variableName)
    {
        return Unwrap(expression)
            .DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(identifier => identifier.Identifier.ValueText == variableName);
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
