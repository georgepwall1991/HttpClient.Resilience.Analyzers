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

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var declaration = node.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>();

        if (declaration is null ||
            declaration.UsingKeyword != default ||
            declaration.Declaration.Variables.Count != 1)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Dispose response with using declaration",
                cancellationToken => AddUsingDeclarationAsync(context.Document, declaration, cancellationToken),
                nameof(HCR060_DisposeResponseCodeFixProvider)),
            diagnostic);
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
}
