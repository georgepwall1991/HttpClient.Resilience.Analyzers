using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace HttpClient.Resilience.Analyzers.CodeFixes;

/// <summary>
/// Shares the top-level statement merge used by the response and stream disposal
/// fixes: an adjacent uninitialized declaration and assignment become one
/// <c>using</c> declaration. Merges are withheld when preprocessor directives
/// guard either statement.
/// </summary>
internal static class TopLevelUsingDeclarationMerge
{
    internal static bool TryGetAdjacentDeclaration(
        AssignmentExpressionSyntax? assignment,
        out CompilationUnitSyntax compilationUnit,
        out GlobalStatementSyntax declarationStatement,
        out LocalDeclarationStatementSyntax declaration,
        out GlobalStatementSyntax assignmentStatement)
    {
        compilationUnit = null!;
        declarationStatement = null!;
        declaration = null!;
        assignmentStatement = null!;

        if (assignment?.Left is not IdentifierNameSyntax identifier ||
            assignment.Parent is not ExpressionStatementSyntax statement ||
            statement.Parent is not GlobalStatementSyntax assignmentGlobal ||
            assignmentGlobal.Parent is not CompilationUnitSyntax root)
        {
            return false;
        }

        var members = root.Members;
        var assignmentIndex = members.IndexOf(assignmentGlobal);
        if (assignmentIndex <= 0 ||
            members[assignmentIndex - 1] is not GlobalStatementSyntax previousGlobal ||
            previousGlobal.Statement is not LocalDeclarationStatementSyntax previousDeclaration ||
            previousDeclaration.UsingKeyword != default)
        {
            return false;
        }

        var variables = previousDeclaration.Declaration.Variables;
        if (variables.Count != 1 ||
            variables[0].Initializer is not null ||
            variables[0].Identifier.ValueText != identifier.Identifier.ValueText)
        {
            return false;
        }

        if (ContainsDirectiveTrivia(previousGlobal) ||
            ContainsDirectiveTrivia(assignmentGlobal))
        {
            return false;
        }

        compilationUnit = root;
        declarationStatement = previousGlobal;
        declaration = previousDeclaration;
        assignmentStatement = assignmentGlobal;
        return true;
    }

    internal static bool ContainsDirectiveTrivia(SyntaxNode node)
    {
        return node.DescendantTrivia().Any(trivia => trivia.IsDirective);
    }

    internal static async Task<Document> MergeDeclarationAndAssignmentAsync(
        Document document,
        CompilationUnitSyntax compilationUnit,
        GlobalStatementSyntax declarationStatement,
        LocalDeclarationStatementSyntax declaration,
        AssignmentExpressionSyntax assignment,
        GlobalStatementSyntax assignmentStatement,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var variable = declaration.Declaration.Variables[0]
            .WithInitializer(SyntaxFactory.EqualsValueClause(assignment.Right.WithoutTrivia()));
        var assignmentTrivia = assignmentStatement
            .GetLeadingTrivia()
            .AddRange(assignmentStatement.GetTrailingTrivia());
        var usingDeclaration = declaration
            .WithDeclaration(declaration.Declaration.WithVariables(SyntaxFactory.SingletonSeparatedList(variable)))
            .WithUsingKeyword(SyntaxFactory.Token(SyntaxKind.UsingKeyword).WithTrailingTrivia(SyntaxFactory.Space))
            .WithTrailingTrivia(assignmentTrivia)
            .WithAdditionalAnnotations(Formatter.Annotation);
        var usingStatement = declarationStatement.WithStatement(usingDeclaration);
        var members = compilationUnit.Members;
        // Remove by index: Replace re-wraps the remaining elements, so a
        // subsequent Remove by node reference would no longer match.
        var updatedMembers = members
            .Replace(declarationStatement, usingStatement)
            .RemoveAt(members.IndexOf(assignmentStatement));
        var updatedRoot = compilationUnit.WithMembers(updatedMembers);

        return document.WithSyntaxRoot(updatedRoot);
    }
}
