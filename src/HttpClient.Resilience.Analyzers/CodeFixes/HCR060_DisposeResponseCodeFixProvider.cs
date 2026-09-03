using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HttpClient.Resilience.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace HttpClient.Resilience.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HCR060_DisposeResponseCodeFixProvider))]
[Shared]
public sealed class HCR060_DisposeResponseCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DiagnosticIds.HCR060);

    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan);
            var declaration = node.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>();

            if (declaration is not null &&
                declaration.UsingKeyword == default &&
                declaration.Declaration.Variables.Count == 1)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Dispose response with using declaration",
                        cancellationToken => AddUsingDeclarationAsync(context.Document, declaration, cancellationToken),
                        nameof(HCR060_DisposeResponseCodeFixProvider)),
                    diagnostic);
                continue;
            }

            var assignment = node.FirstAncestorOrSelf<AssignmentExpressionSyntax>();
            if (TryGetAdjacentDeclaration(
                    assignment,
                    out var block,
                    out var adjacentDeclaration,
                    out var assignmentStatement))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Dispose response with using declaration",
                        cancellationToken => MergeDeclarationAndAssignmentAsync(
                            context.Document,
                            block,
                            adjacentDeclaration,
                            assignment!,
                            assignmentStatement,
                            cancellationToken),
                        nameof(HCR060_DisposeResponseCodeFixProvider)),
                    diagnostic);
                continue;
            }

            if (TryGetAdjacentTopLevelDeclaration(
                    assignment,
                    out var compilationUnit,
                    out var declarationStatement,
                    out var topLevelDeclaration,
                    out var topLevelAssignmentStatement))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Dispose response with using declaration",
                        cancellationToken => MergeTopLevelDeclarationAndAssignmentAsync(
                            context.Document,
                            compilationUnit,
                            declarationStatement,
                            topLevelDeclaration,
                            assignment!,
                            topLevelAssignmentStatement,
                            cancellationToken),
                        nameof(HCR060_DisposeResponseCodeFixProvider)),
                    diagnostic);
            }
        }
    }

    private static bool TryGetAdjacentDeclaration(
        AssignmentExpressionSyntax? assignment,
        out BlockSyntax block,
        out LocalDeclarationStatementSyntax declaration,
        out ExpressionStatementSyntax assignmentStatement)
    {
        block = null!;
        declaration = null!;
        assignmentStatement = null!;

        if (assignment?.Left is not IdentifierNameSyntax identifier ||
            assignment.Parent is not ExpressionStatementSyntax statement ||
            statement.Parent is not BlockSyntax containingBlock)
        {
            return false;
        }

        var assignmentIndex = containingBlock.Statements.IndexOf(statement);
        if (assignmentIndex <= 0 ||
            containingBlock.Statements[assignmentIndex - 1] is not LocalDeclarationStatementSyntax previousDeclaration ||
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

        if (ContainsDirectiveTrivia(previousDeclaration) ||
            ContainsDirectiveTrivia(statement))
        {
            return false;
        }

        block = containingBlock;
        declaration = previousDeclaration;
        assignmentStatement = statement;
        return true;
    }

    private static bool TryGetAdjacentTopLevelDeclaration(
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

    private static bool ContainsDirectiveTrivia(SyntaxNode node)
    {
        return node.DescendantTrivia().Any(trivia => trivia.IsDirective);
    }

    private static async Task<Document> AddUsingDeclarationAsync(
        Document document,
        LocalDeclarationStatementSyntax declaration,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var usingDeclaration = declaration
            .WithUsingKeyword(SyntaxFactory.Token(SyntaxKind.UsingKeyword).WithTrailingTrivia(SyntaxFactory.Space))
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(declaration, usingDeclaration));
    }

    private static async Task<Document> MergeDeclarationAndAssignmentAsync(
        Document document,
        BlockSyntax block,
        LocalDeclarationStatementSyntax declaration,
        AssignmentExpressionSyntax assignment,
        ExpressionStatementSyntax assignmentStatement,
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
        var statements = block.Statements
            .Replace(declaration, usingDeclaration)
            .RemoveAt(block.Statements.IndexOf(assignmentStatement));
        var updatedBlock = block.WithStatements(statements);

        return document.WithSyntaxRoot(root.ReplaceNode(block, updatedBlock));
    }

    private static async Task<Document> MergeTopLevelDeclarationAndAssignmentAsync(
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
