using System.Collections.Immutable;
using System.Composition;
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

        var diagnostic = context.Diagnostics[0];
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
            return;
        }

        var assignment = node.FirstAncestorOrSelf<AssignmentExpressionSyntax>();
        if (!TryGetAdjacentDeclaration(
                assignment,
                out var block,
                out var adjacentDeclaration,
                out var assignmentStatement))
        {
            return;
        }

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

        block = containingBlock;
        declaration = previousDeclaration;
        assignmentStatement = statement;
        return true;
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
        var usingDeclaration = declaration
            .WithDeclaration(declaration.Declaration.WithVariables(SyntaxFactory.SingletonSeparatedList(variable)))
            .WithUsingKeyword(SyntaxFactory.Token(SyntaxKind.UsingKeyword).WithTrailingTrivia(SyntaxFactory.Space))
            .WithTrailingTrivia(assignmentStatement.GetTrailingTrivia())
            .WithAdditionalAnnotations(Formatter.Annotation);
        var statements = block.Statements
            .Replace(declaration, usingDeclaration)
            .Remove(assignmentStatement);
        var updatedBlock = block.WithStatements(statements);

        return document.WithSyntaxRoot(root.ReplaceNode(block, updatedBlock));
    }
}
