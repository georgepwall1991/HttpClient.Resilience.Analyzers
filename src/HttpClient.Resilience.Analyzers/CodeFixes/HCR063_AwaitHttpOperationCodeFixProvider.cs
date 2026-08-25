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

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan);
            var blockingExpression = GetBlockingExpression(node, out var operation);
            if (blockingExpression is null || operation is null || !IsInsideAsyncFunction(blockingExpression))
            {
                continue;
            }

            // Match the surrounding convention: methods that already use ConfigureAwait(false)
            // get an awaited call that preserves it.
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Await the HTTP operation",
                    cancellationToken => ReplaceWithAwaitAsync(
                        context.Document,
                        blockingExpression,
                        operation,
                        appendConfigureAwait: UsesConfigureAwait(blockingExpression),
                        cancellationToken),
                    nameof(HCR063_AwaitHttpOperationCodeFixProvider)),
                diagnostic);
        }
    }

    private static ExpressionSyntax? GetBlockingExpression(SyntaxNode node, out ExpressionSyntax? operation)
    {
        var resultAccess = node.FirstAncestorOrSelf<MemberAccessExpressionSyntax>();
        if (resultAccess?.Name.Identifier.ValueText == "Result")
        {
            operation = resultAccess.Expression;
            return resultAccess;
        }

        var getResultInvocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (getResultInvocation is
            {
                ArgumentList.Arguments.Count: 0,
                Parent: ExpressionStatementSyntax,
                Expression: MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: "Wait"
                } waitAccess
            })
        {
            operation = waitAccess.Expression;
            return getResultInvocation;
        }

        if (getResultInvocation?.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "GetResult",
                Expression: InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax
                    {
                        Name.Identifier.ValueText: "GetAwaiter"
                    } getAwaiterAccess
                }
            })
        {
            operation = getAwaiterAccess.Expression;
            return getResultInvocation;
        }

        operation = null;
        return null;
    }

    private static bool IsInsideAsyncFunction(ExpressionSyntax blockingExpression)
    {
        return blockingExpression.Ancestors()
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

    private static bool UsesConfigureAwait(ExpressionSyntax blockingExpression)
    {
        return blockingExpression.Ancestors()
            .OfType<MemberDeclarationSyntax>()
            .FirstOrDefault()?
            .DescendantNodes()
            .Any(node => node is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "ConfigureAwait"
            } && node.SpanStart < blockingExpression.SpanStart) == true;
    }

    private static bool EndsWithConfigureAwait(ExpressionSyntax expression)
    {
        return expression is InvocationExpressionSyntax
        {
            ArgumentList.Arguments.Count: 1,
            Expression: MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "ConfigureAwait"
            }
        };
    }

    private static async Task<Document> ReplaceWithAwaitAsync(
        Document document,
        ExpressionSyntax blockingExpression,
        ExpressionSyntax operation,
        bool appendConfigureAwait,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        if (appendConfigureAwait && !EndsWithConfigureAwait(operation))
        {
            operation = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    operation.WithoutTrivia(),
                    SyntaxFactory.IdentifierName("ConfigureAwait")),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(
                            SyntaxKind.FalseLiteralExpression)))));
        }

        ExpressionSyntax replacement = SyntaxFactory.AwaitExpression(operation.WithoutTrivia());
        if (blockingExpression.Parent is MemberAccessExpressionSyntax or ElementAccessExpressionSyntax)
        {
            replacement = SyntaxFactory.ParenthesizedExpression(replacement);
        }

        replacement = replacement
            .WithTriviaFrom(blockingExpression)
            .WithAdditionalAnnotations(Formatter.Annotation);
        var comments = operation
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

        return document.WithSyntaxRoot(root.ReplaceNode(blockingExpression, replacement));
    }
}
