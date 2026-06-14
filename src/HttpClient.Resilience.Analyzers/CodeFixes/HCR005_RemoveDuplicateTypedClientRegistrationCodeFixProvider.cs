using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using HttpClient.Resilience.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace HttpClient.Resilience.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HCR005_RemoveDuplicateTypedClientRegistrationCodeFixProvider))]
[Shared]
public sealed class HCR005_RemoveDuplicateTypedClientRegistrationCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DiagnosticIds.HCR005);

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
        var statement = node.FirstAncestorOrSelf<ExpressionStatementSyntax>();

        if (statement is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Remove duplicate typed-client registration",
                cancellationToken => RemoveStatementAsync(context.Document, statement, cancellationToken),
                nameof(HCR005_RemoveDuplicateTypedClientRegistrationCodeFixProvider)),
            diagnostic);
    }

    private static async Task<Document> RemoveStatementAsync(
        Document document,
        ExpressionStatementSyntax statement,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        return document.WithSyntaxRoot(root.RemoveNode(statement, SyntaxRemoveOptions.KeepNoTrivia)!.WithAdditionalAnnotations(Formatter.Annotation));
    }
}
