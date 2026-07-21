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
        "ReadFromJsonAsync",
        "ReadAsStream",
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
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var variable = node
            .FirstAncestorOrSelf<VariableDeclaratorSyntax>();
        var declaration = variable?.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>();
        var block = declaration?.Parent as BlockSyntax;

        if (variable is not null &&
            declaration is not null &&
            block is not null &&
            declaration.Declaration.Variables.Count == 1 &&
            HasContentRead(block, declaration, variable.Identifier.ValueText))
        {
            RegisterCodeFix(context, diagnostic, block, declaration, variable.Identifier);
            return;
        }

        var responseIdentifier = node.FirstAncestorOrSelf<IdentifierNameSyntax>();
        var assignment = responseIdentifier?.FirstAncestorOrSelf<AssignmentExpressionSyntax>();
        var assignmentStatement = assignment?.Parent as ExpressionStatementSyntax;
        block = assignmentStatement?.Parent as BlockSyntax;
        if (responseIdentifier is null ||
            assignment?.Left != responseIdentifier ||
            assignmentStatement is null ||
            block is null ||
            !HasContentRead(block, assignmentStatement, responseIdentifier.Identifier.ValueText))
        {
            return;
        }

        RegisterCodeFix(context, diagnostic, block, assignmentStatement, responseIdentifier.Identifier);
    }

    private static void RegisterCodeFix(
        CodeFixContext context,
        Diagnostic diagnostic,
        BlockSyntax block,
        StatementSyntax responseAcquisition,
        SyntaxToken responseIdentifier)
    {
        context.RegisterCodeFix(
            CodeAction.Create(
                $"Call '{responseIdentifier.ValueText}.EnsureSuccessStatusCode()'",
                cancellationToken => AddSuccessCheckAsync(
                    context.Document,
                    block,
                    responseAcquisition,
                    responseIdentifier,
                    cancellationToken),
                nameof(HCR061_EnsureSuccessStatusCodeCodeFixProvider)),
            diagnostic);
    }

    private static bool HasContentRead(
        BlockSyntax block,
        StatementSyntax responseAcquisition,
        string responseName)
    {
        return block
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.SpanStart > responseAcquisition.SpanStart)
            .Where(invocation => invocation.Expression is MemberAccessExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax responseIdentifier,
                    Name.Identifier.ValueText: "Content"
                },
                Name: SimpleNameSyntax methodName
            } &&
                responseIdentifier.Identifier.ValueText == responseName &&
                ContentReadMethodNames.Contains(methodName.Identifier.ValueText, System.StringComparer.Ordinal))
            .Any();
    }

    private static async Task<Document> AddSuccessCheckAsync(
        Document document,
        BlockSyntax block,
        StatementSyntax responseAcquisition,
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
        var acquisitionIndex = block.Statements.IndexOf(responseAcquisition);
        var updatedBlock = block.WithStatements(block.Statements.Insert(acquisitionIndex + 1, successCheck));

        return document.WithSyntaxRoot(root.ReplaceNode(block, updatedBlock));
    }
}
