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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HCR064_PassCancellationTokenCodeFixProvider))]
[Shared]
public sealed class HCR064_PassCancellationTokenCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DiagnosticIds.HCR064);

    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null)
        {
            return;
        }

        var diagnostic = context.Diagnostics[0];
        var invocation = root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null)
        {
            return;
        }

        var cancellationTokens = semanticModel.LookupSymbols(invocation.SpanStart)
            .Where(symbol => symbol is ILocalSymbol or IParameterSymbol)
            .Where(symbol => IsCancellationToken(symbol switch
            {
                ILocalSymbol local => local.Type,
                IParameterSymbol parameter => parameter.Type,
                _ => null
            }))
            .ToArray();

        if (cancellationTokens.Length == 0)
        {
            return;
        }

        foreach (var cancellationTokenSymbol in cancellationTokens.OrderBy(
                     symbol => symbol.Name,
                     System.StringComparer.Ordinal))
        {
            var tokenExpression = CreateIdentifierName(cancellationTokenSymbol.Name);
            var tokenArgument = SyntaxFactory.Argument(tokenExpression)
                .WithNameColon(SyntaxFactory.NameColon(SyntaxFactory.IdentifierName("cancellationToken")));

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Pass '{cancellationTokenSymbol.Name}' cancellation token",
                    cancellationToken => AddCancellationTokenAsync(
                        context.Document,
                        invocation,
                        tokenArgument,
                        cancellationToken),
                    $"{nameof(HCR064_PassCancellationTokenCodeFixProvider)}.{cancellationTokenSymbol.Name}"),
                diagnostic);
        }
    }

    private static async Task<Document> AddCancellationTokenAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        ArgumentSyntax tokenArgument,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var updatedInvocation = invocation
            .WithArgumentList(invocation.ArgumentList.AddArguments(tokenArgument))
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, updatedInvocation));
    }

    private static IdentifierNameSyntax CreateIdentifierName(string name)
    {
        var text = SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;
        return SyntaxFactory.IdentifierName(SyntaxFactory.Identifier(text));
    }

    private static bool IsCancellationToken(ITypeSymbol? type)
    {
        return type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::System.Threading.CancellationToken";
    }
}
