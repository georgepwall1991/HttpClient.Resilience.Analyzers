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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HCR041_DisableUnsafeMethodRetriesCodeFixProvider))]
[Shared]
public sealed class HCR041_DisableUnsafeMethodRetriesCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DiagnosticIds.HCR041);

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
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();

        if (invocation is null || invocation.ArgumentList.Arguments.Count != 0)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Disable retries for unsafe HTTP methods",
                cancellationToken => DisableUnsafeMethodRetriesAsync(context.Document, invocation, cancellationToken),
                nameof(HCR041_DisableUnsafeMethodRetriesCodeFixProvider)),
            diagnostic);
    }

    private static async Task<Document> DisableUnsafeMethodRetriesAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var argument = SyntaxFactory.Argument(
            SyntaxFactory.ParseExpression("options => options.Retry.DisableForUnsafeHttpMethods()"));
        var newInvocation = invocation
            .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(argument)))
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, newInvocation));
    }
}
