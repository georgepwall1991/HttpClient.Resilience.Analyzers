using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HttpClient.Resilience.Analyzers.CodeFixes;

/// <summary>
/// Decides whether a `using` declaration fix is safe for a disposable local.
/// Disposing at scope end breaks callers when the value outlives the block,
/// so no automatic fix is offered once the variable escapes.
/// </summary>
internal static class UsingDeclarationEscapeGate
{
    internal static bool VariableEscapesScope(SyntaxNode node, string variableName)
    {
        if (string.IsNullOrEmpty(variableName))
        {
            return false;
        }

        SyntaxNode? scope = node.FirstAncestorOrSelf<BlockSyntax>();
        scope ??= node.FirstAncestorOrSelf<CompilationUnitSyntax>();
        if (scope is null)
        {
            return false;
        }

        return scope.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(identifier => identifier.Identifier.ValueText == variableName)
            .Any(IsTransferredOut);
    }

    private static bool IsTransferredOut(IdentifierNameSyntax identifier)
    {
        var child = (SyntaxNode)identifier;
        for (var current = identifier.Parent; current is not null; child = current, current = current.Parent)
        {
            // Merely calling a member on the value keeps ownership local.
            if (current is MemberAccessExpressionSyntax memberAccess &&
                ReferenceEquals(memberAccess.Expression, child))
            {
                return false;
            }

            if (current is ArgumentSyntax)
            {
                return true;
            }

            if (current is ReturnStatementSyntax)
            {
                return true;
            }

            // Object, collection, array, and `with` initializers all store into
            // the new aggregate (WithInitializerExpression shares this node type).
            if (current is InitializerExpressionSyntax)
            {
                return true;
            }

            if (current is AssignmentExpressionSyntax assignment)
            {
                // Initializer members (`new Foo { Bar = value }`) store into the
                // new aggregate even though the member reads as an identifier.
                if (assignment.Parent is InitializerExpressionSyntax)
                {
                    return true;
                }

                // `variable = ...` keeps ownership local; any other target
                // (member, element, or container) leaks it.
                return assignment.Left is not IdentifierNameSyntax;
            }

            if (current is not ParenthesizedExpressionSyntax and not EqualsValueClauseSyntax)
            {
                return false;
            }
        }

        return false;
    }
}
