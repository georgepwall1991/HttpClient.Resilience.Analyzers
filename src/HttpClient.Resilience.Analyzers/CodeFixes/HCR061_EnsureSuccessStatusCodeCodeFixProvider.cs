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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HCR061_EnsureSuccessStatusCodeCodeFixProvider))]
[Shared]
public sealed class HCR061_EnsureSuccessStatusCodeCodeFixProvider : CodeFixProvider
{
    private static readonly string[] ContentReadMethodNames =
    {
        "CopyToAsync",
        "LoadIntoBufferAsync",
        "ReadAsByteArrayAsync",
        "ReadAsStreamAsync",
        "ReadAsStringAsync"
    };

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DiagnosticIds.HCR061);

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
        var variable = root.FindNode(diagnostic.Location.SourceSpan)
            .FirstAncestorOrSelf<VariableDeclaratorSyntax>();
        var declaration = variable?.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>();
        var block = declaration?.Parent as BlockSyntax;

        if (variable is null ||
            declaration is null ||
            block is null ||
            declaration.Declaration.Variables.Count != 1 ||
            !HasContentRead(block, declaration, variable.Identifier.ValueText))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                $"Call '{variable.Identifier.ValueText}.EnsureSuccessStatusCode()'",
                cancellationToken => AddSuccessCheckAsync(
                    context.Document,
                    block,
                    declaration,
                    variable.Identifier,
                    cancellationToken),
                nameof(HCR061_EnsureSuccessStatusCodeCodeFixProvider)),
            diagnostic);
    }

    private static bool HasContentRead(
        BlockSyntax block,
        LocalDeclarationStatementSyntax declaration,
        string responseName)
    {
        return block
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.SpanStart > declaration.SpanStart)
            .Where(invocation => invocation.Expression is MemberAccessExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax responseIdentifier,
                    Name.Identifier.ValueText: "Content"
                },
                Name: IdentifierNameSyntax methodName
            } &&
                responseIdentifier.Identifier.ValueText == responseName &&
                ContentReadMethodNames.Contains(methodName.Identifier.ValueText, System.StringComparer.Ordinal))
            .Any();
    }

    private static async Task<Document> AddSuccessCheckAsync(
        Document document,
        BlockSyntax block,
        LocalDeclarationStatementSyntax declaration,
        SyntaxToken responseIdentifier,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var successCheck = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName(responseIdentifier.WithoutTrivia()),
                        SyntaxFactory.IdentifierName("EnsureSuccessStatusCode"))))
            .WithAdditionalAnnotations(Formatter.Annotation);
        var declarationIndex = block.Statements.IndexOf(declaration);
        var updatedBlock = block.WithStatements(block.Statements.Insert(declarationIndex + 1, successCheck));

        return document.WithSyntaxRoot(root.ReplaceNode(block, updatedBlock));
    }
}
