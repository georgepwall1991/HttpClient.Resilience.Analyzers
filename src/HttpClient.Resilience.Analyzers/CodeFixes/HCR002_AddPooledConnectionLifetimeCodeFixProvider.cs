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

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var variable = node.FirstAncestorOrSelf<VariableDeclaratorSyntax>();

        if (variable?.Initializer is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Configure PooledConnectionLifetime",
                cancellationToken => ConfigurePooledConnectionLifetimeAsync(context.Document, variable, cancellationToken),
                nameof(HCR002_AddPooledConnectionLifetimeCodeFixProvider)),
            diagnostic);
    }

    private static async Task<Document> ConfigurePooledConnectionLifetimeAsync(
        Document document,
        VariableDeclaratorSyntax variable,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var replacement = SyntaxFactory.ParseExpression(
                "new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = System.TimeSpan.FromMinutes(2) })")
            .WithTriviaFrom(variable.Initializer!.Value)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newVariable = variable.WithInitializer(variable.Initializer.WithValue(replacement));
        return document.WithSyntaxRoot(root.ReplaceNode(variable, newVariable));
    }
}
