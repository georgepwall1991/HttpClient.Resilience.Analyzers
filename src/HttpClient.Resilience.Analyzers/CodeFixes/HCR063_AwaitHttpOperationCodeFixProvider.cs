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
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var blockingExpression = GetBlockingExpression(node, out var operation);
        if (blockingExpression is null || operation is null || !IsInsideAsyncFunction(blockingExpression))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Await the HTTP operation",
                cancellationToken => ReplaceWithAwaitAsync(
                    context.Document,
                    blockingExpression,
                    operation,
                    cancellationToken),
                nameof(HCR063_AwaitHttpOperationCodeFixProvider)),
            diagnostic);
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

    private static async Task<Document> ReplaceWithAwaitAsync(
        Document document,
        ExpressionSyntax blockingExpression,
        ExpressionSyntax operation,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        ExpressionSyntax replacement = SyntaxFactory.AwaitExpression(operation.WithoutTrivia());
        if (blockingExpression.Parent is MemberAccessExpressionSyntax or ElementAccessExpressionSyntax)
        {
            replacement = SyntaxFactory.ParenthesizedExpression(replacement);
        }

        replacement = replacement
            .WithTriviaFrom(blockingExpression)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(blockingExpression, replacement));
    }
}
