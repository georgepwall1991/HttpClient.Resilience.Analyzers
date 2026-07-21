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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HCR063_AwaitHttpOperationCodeFixProvider))]
[Shared]
public sealed class HCR063_AwaitHttpOperationCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DiagnosticIds.HCR063);

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
        var resultAccess = root.FindNode(diagnostic.Location.SourceSpan)
            .FirstAncestorOrSelf<MemberAccessExpressionSyntax>();

        if (resultAccess is null ||
            resultAccess.Name.Identifier.ValueText != "Result" ||
            !IsInsideAsyncFunction(resultAccess))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Await the HTTP operation",
                cancellationToken => ReplaceResultWithAwaitAsync(
                    context.Document,
                    resultAccess,
                    cancellationToken),
                nameof(HCR063_AwaitHttpOperationCodeFixProvider)),
            diagnostic);
    }

    private static bool IsInsideAsyncFunction(MemberAccessExpressionSyntax resultAccess)
    {
        return resultAccess.Ancestors()
            .FirstOrDefault(node => node is BaseMethodDeclarationSyntax or
                LocalFunctionStatementSyntax or
                AnonymousFunctionExpressionSyntax) switch
        {
            BaseMethodDeclarationSyntax method => method.Modifiers.Any(SyntaxKind.AsyncKeyword),
            LocalFunctionStatementSyntax localFunction => localFunction.Modifiers.Any(SyntaxKind.AsyncKeyword),
            AnonymousFunctionExpressionSyntax anonymousFunction => anonymousFunction.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword),
            _ => false
        };
    }

    private static async Task<Document> ReplaceResultWithAwaitAsync(
        Document document,
        MemberAccessExpressionSyntax resultAccess,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        ExpressionSyntax replacement = SyntaxFactory.AwaitExpression(resultAccess.Expression.WithoutTrivia());
        if (resultAccess.Parent is MemberAccessExpressionSyntax or ElementAccessExpressionSyntax)
        {
            replacement = SyntaxFactory.ParenthesizedExpression(replacement);
        }

        replacement = replacement
            .WithTriviaFrom(resultAccess)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(resultAccess, replacement));
    }
}
