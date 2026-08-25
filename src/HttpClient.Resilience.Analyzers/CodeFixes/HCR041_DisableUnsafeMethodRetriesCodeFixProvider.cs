using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Models;
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

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan);
            var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();

            if (invocation?.Expression is not MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: "AddStandardResilienceHandler"
                })
            {
                continue;
            }

            if (invocation.ArgumentList.Arguments.Count == 0)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Disable retries for unsafe HTTP methods",
                        cancellationToken => DisableUnsafeMethodRetriesAsync(context.Document, invocation, cancellationToken),
                        nameof(HCR041_DisableUnsafeMethodRetriesCodeFixProvider)),
                    diagnostic);
                continue;
            }

            // A configure delegate already exists: inject the guard as its first statement
            // instead of replacing the user's configuration.
            var lambda = SyntaxTransparency.Unwrap(invocation.ArgumentList.Arguments[0].Expression) as LambdaExpressionSyntax;
            var parameterName = lambda switch
            {
                SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
                ParenthesizedLambdaExpressionSyntax parenthesized when
                    parenthesized.ParameterList.Parameters.Count == 1 =>
                    parenthesized.ParameterList.Parameters[0].Identifier.ValueText,
                _ => null
            };

            if (parameterName is null ||
                lambda!.Body is not BlockSyntax body ||
                BodyAlreadyDisablesRetries(body, parameterName))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Disable retries for unsafe HTTP methods",
                    cancellationToken => InjectDisableIntoConfiguredLambdaAsync(
                        context.Document,
                        invocation,
                        lambda,
                        body,
                        parameterName,
                        cancellationToken),
                    nameof(HCR041_DisableUnsafeMethodRetriesCodeFixProvider) + ".ConfigureDelegate"),
                diagnostic);
        }
    }

    private static bool BodyAlreadyDisablesRetries(BlockSyntax body, string parameterName)
    {
        return body.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => invocation.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "DisableForUnsafeHttpMethods",
                Expression: MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax receiver
                }
            } && receiver.Identifier.ValueText == parameterName) ||
            body.DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Any(assignment => assignment.Left is MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: "ShouldHandle"
                });
    }

    private static async Task<Document> InjectDisableIntoConfiguredLambdaAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        LambdaExpressionSyntax lambda,
        BlockSyntax body,
        string parameterName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var disableCall = SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName(parameterName),
                        SyntaxFactory.IdentifierName("Retry")),
                    SyntaxFactory.IdentifierName("DisableForUnsafeHttpMethods"))));

        var updatedLambda = lambda.WithBody(body.WithStatements(body.Statements.Insert(0, disableCall)))
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(lambda, updatedLambda));
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
            CodeFixExpressionFactory.CreateDisableForUnsafeHttpMethodsLambda());
        var newInvocation = invocation
            .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(argument)))
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, newInvocation));
    }
}
