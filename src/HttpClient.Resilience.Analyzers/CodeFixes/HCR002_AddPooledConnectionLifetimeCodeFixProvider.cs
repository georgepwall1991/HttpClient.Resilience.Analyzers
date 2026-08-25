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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HCR002_AddPooledConnectionLifetimeCodeFixProvider))]
[Shared]
public sealed class HCR002_AddPooledConnectionLifetimeCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DiagnosticIds.HCR002);

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

            var variable = node.FirstAncestorOrSelf<VariableDeclaratorSyntax>();
            if (variable?.Initializer is not null &&
                CanSafelyConfigureInitializer(variable.Initializer.Value))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Configure PooledConnectionLifetime",
                        cancellationToken => ReplaceInitializerValueAsync(
                            context.Document,
                            variable.Initializer,
                            cancellationToken),
                        nameof(HCR002_AddPooledConnectionLifetimeCodeFixProvider)),
                    diagnostic);
                continue;
            }

            var property = node.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
            if (property?.Initializer is not null &&
                CanSafelyConfigureInitializer(property.Initializer.Value))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Configure PooledConnectionLifetime",
                        cancellationToken => ReplaceInitializerValueAsync(
                            context.Document,
                            property.Initializer,
                            cancellationToken),
                        nameof(HCR002_AddPooledConnectionLifetimeCodeFixProvider)),
                    diagnostic);
            }
        }
    }

    private static bool CanSafelyConfigureInitializer(ExpressionSyntax expression)
    {
        // Replacing the whole expression would drop any comment inside it, so withhold the
        // fix when the initializer carries significant internal trivia.
        return expression is BaseObjectCreationExpressionSyntax creation &&
            creation.ArgumentList?.Arguments.Count == 0 &&
            creation.Initializer is null &&
            !creation.DescendantTrivia().Any(trivia => trivia.Kind() is
                SyntaxKind.SingleLineCommentTrivia or
                SyntaxKind.MultiLineCommentTrivia or
                SyntaxKind.SingleLineDocumentationCommentTrivia or
                SyntaxKind.MultiLineDocumentationCommentTrivia);
    }

    private static async Task<Document> ReplaceInitializerValueAsync(
        Document document,
        EqualsValueClauseSyntax initializer,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var replacement = SyntaxFactory.ParseExpression(
                "new global::System.Net.Http.HttpClient(new global::System.Net.Http.SocketsHttpHandler { PooledConnectionLifetime = global::System.TimeSpan.FromMinutes(2) })")
            .WithTriviaFrom(initializer.Value)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(
            root.ReplaceNode(initializer, initializer.WithValue(replacement)));
    }
}
