using System.Collections.Generic;
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
                var escapes = VariableEscapesScope(
                    node,
                    declaration.Declaration.Variables[0].Identifier.ValueText);
                if (!escapes)
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            "Dispose response with using declaration",
                            cancellationToken => AddUsingDeclarationAsync(context.Document, declaration, cancellationToken),
                            nameof(HCR060_DisposeResponseCodeFixProvider)),
                        diagnostic);
                }

                continue;
            }

            var assignment = node.FirstAncestorOrSelf<AssignmentExpressionSyntax>();
            if (!TryGetAdjacentDeclaration(
                    assignment,
                    out var block,
                    out var adjacentDeclaration,
                    out var assignmentStatement))
            {
                continue;
            }

            // For `_ = await response.Content...` the disposed response appears on the right
            // side. Only locals declared in this block can be affected by a using declaration;
            // parameters and outer locals are irrelevant.
            var blockLocalNames = new HashSet<string>(
                block.DescendantNodes()
                    .OfType<VariableDeclaratorSyntax>()
                    .Select(variable => variable.Identifier.ValueText),
                System.StringComparer.Ordinal);
            var referencedNames = assignment.Right.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Select(identifier => identifier.Identifier.ValueText)
                .Where(name => blockLocalNames.Contains(name))
                .Distinct()
                .ToList();

            if (referencedNames.Any(name => VariableEscapesScope(node, name)))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Dispose response with using declaration",
                    cancellationToken => MergeDeclarationAndAssignmentAsync(
                        context.Document,
                        block,
                        adjacentDeclaration,
                        assignment,
                        assignmentStatement,
                        cancellationToken),
                    nameof(HCR060_DisposeResponseCodeFixProvider)),
                diagnostic);
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

        block = containingBlock;
        declaration = previousDeclaration;
        assignmentStatement = statement;
        return true;
    }

    private static bool VariableEscapesScope(SyntaxNode node, string variableName)
    {
        if (variableName.Length == 0)
        {
            return false;
        }

        var block = node.FirstAncestorOrSelf<BlockSyntax>();
        if (block is null)
        {
            return false;
        }

        // Disposing at scope end breaks callers when the response outlives the block:
        // returned directly or stored into a member or another container.
        return block.DescendantNodes()
            .Any(descendant => descendant switch
            {
                ReturnStatementSyntax { Expression: IdentifierNameSyntax returned } =>
                    returned.Identifier.ValueText == variableName,
                AssignmentExpressionSyntax assignment when assignment.Left is not IdentifierNameSyntax =>
                    assignment.Right.DescendantNodesAndSelf()
                        .OfType<IdentifierNameSyntax>()
                        .Any(identifier => identifier.Identifier.ValueText == variableName),
                _ => false
            });
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
}
