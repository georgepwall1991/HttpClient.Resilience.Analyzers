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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HCR040_RemoveDuplicateStandardResilienceHandlerCodeFixProvider))]
[Shared]
public sealed class HCR040_RemoveDuplicateStandardResilienceHandlerCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DiagnosticIds.HCR040);

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
            var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();

            if (invocation?.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Remove duplicate resilience handler",
                    cancellationToken => RemoveDuplicateInvocationAsync(context.Document, invocation, memberAccess.Expression, cancellationToken),
                    nameof(HCR040_RemoveDuplicateStandardResilienceHandlerCodeFixProvider)),
                diagnostic);
        }
    }

    private static async Task<Document> RemoveDuplicateInvocationAsync(
        Document document,
        InvocationExpressionSyntax duplicateInvocation,
        ExpressionSyntax previousInvocation,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        if (duplicateInvocation.Parent is ExpressionStatementSyntax expressionStatement)
        {
            return document.WithSyntaxRoot(root.RemoveNode(expressionStatement, SyntaxRemoveOptions.KeepExteriorTrivia) ?? root);
        }

        var replacement = previousInvocation
            .WithTriviaFrom(duplicateInvocation)
            .WithAdditionalAnnotations(Formatter.Annotation);
        var comments = duplicateInvocation
            .DescendantTokens()
            .SelectMany(token => token.LeadingTrivia.Concat(token.TrailingTrivia))
            .Where(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            .ToArray();
        if (comments.Length > 0)
        {
            replacement = replacement.WithLeadingTrivia(
                replacement.GetLeadingTrivia().AddRange(comments));
        }

        return document.WithSyntaxRoot(root.ReplaceNode(duplicateInvocation, replacement));
    }
}
