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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HCR042_ReplaceHedgingWithSafeResilienceCodeFixProvider))]
[Shared]
public sealed class HCR042_ReplaceHedgingWithSafeResilienceCodeFixProvider : CodeFixProvider
{
    public const string Title = "Switch from hedging to standard resilience and disable unsafe-method retries";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DiagnosticIds.HCR042);

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

        if (invocation?.Expression is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "AddStandardHedgingHandler"
            } memberAccess ||
            invocation.ArgumentList.Arguments.Count != 0)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                Title,
                cancellationToken => ReplaceHedgingAsync(context.Document, invocation, memberAccess, cancellationToken),
                nameof(HCR042_ReplaceHedgingWithSafeResilienceCodeFixProvider)),
            diagnostic);
    }

    private static async Task<Document> ReplaceHedgingAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var newMemberAccess = memberAccess.WithName(
            SyntaxFactory.IdentifierName("AddStandardResilienceHandler")
                .WithTriviaFrom(memberAccess.Name));
        var argument = SyntaxFactory.Argument(
            SyntaxFactory.ParseExpression("options => options.Retry.DisableForUnsafeHttpMethods()"));
        var newInvocation = invocation
            .WithExpression(newMemberAccess)
            .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(argument)))
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, newInvocation));
    }
}
