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

/// <summary>
/// Rewrites the flagged <c>AddSingleton</c> registration into <c>AddScoped</c>, giving the
/// consuming service a lifetime compatible with the typed client it resolves.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HCR004_ChangeToScopedLifetimeCodeFixProvider))]
[Shared]
public sealed class HCR004_ChangeToScopedLifetimeCodeFixProvider : CodeFixProvider
{
    public const string Title = "Change singleton registration to scoped";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DiagnosticIds.HCR004);

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

            if (invocation?.Expression is not MemberAccessExpressionSyntax memberAccess ||
                !IsAddSingletonName(memberAccess.Name))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    cancellationToken => ChangeToScopedAsync(context.Document, invocation, memberAccess, cancellationToken),
                    nameof(HCR004_ChangeToScopedLifetimeCodeFixProvider)),
                diagnostic);
        }
    }

    private static bool IsAddSingletonName(SimpleNameSyntax name)
    {
        return name switch
        {
            GenericNameSyntax generic => generic.Identifier.ValueText == "AddSingleton",
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "AddSingleton",
            _ => false
        };
    }

    private static async Task<Document> ChangeToScopedAsync(
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

        SimpleNameSyntax newName = memberAccess.Name switch
        {
            GenericNameSyntax generic => SyntaxFactory.GenericName("AddScoped")
                .WithTypeArgumentList(generic.TypeArgumentList),
            _ => SyntaxFactory.IdentifierName("AddScoped")
        };

        var newMemberAccess = memberAccess.WithName(newName.WithTriviaFrom(memberAccess.Name));
        var newInvocation = invocation
            .WithExpression(newMemberAccess)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, newInvocation));
    }
}
