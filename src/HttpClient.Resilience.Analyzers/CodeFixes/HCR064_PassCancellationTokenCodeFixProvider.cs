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

        var cancellationTokenNoneArgument = invocation.ArgumentList.Arguments
            .FirstOrDefault(argument =>
                IsCancellationToken(semanticModel.GetTypeInfo(argument.Expression, context.CancellationToken).Type) &&
                IsCancellationTokenNone(argument.Expression, semanticModel, context.CancellationToken));

        var cancellationTokens = semanticModel.LookupSymbols(invocation.SpanStart)
            .Where(symbol => symbol is ILocalSymbol or IParameterSymbol)
            .Where(symbol => IsCancellationToken(symbol switch
            {
                ILocalSymbol local => local.Type,
                IParameterSymbol parameter => parameter.Type,
                _ => null
            }) || IsCancellationTokenSource(symbol switch
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
            var symbolType = cancellationTokenSymbol switch
            {
                ILocalSymbol local => local.Type,
                IParameterSymbol parameter => parameter.Type,
                _ => null
            };
            var tokenDisplayName = IsCancellationTokenSource(symbolType)
                ? cancellationTokenSymbol.Name + ".Token"
                : cancellationTokenSymbol.Name;
            ExpressionSyntax tokenExpression = CreateIdentifierName(cancellationTokenSymbol.Name);
            if (IsCancellationTokenSource(symbolType))
            {
                tokenExpression = SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    tokenExpression,
                    SyntaxFactory.IdentifierName("Token"));
            }

            var tokenArgument = SyntaxFactory.Argument(tokenExpression)
                .WithNameColon(SyntaxFactory.NameColon(SyntaxFactory.IdentifierName("cancellationToken")));

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Pass '{tokenDisplayName}' cancellation token",
                    cancellationToken => PassCancellationTokenAsync(
                        context.Document,
                        invocation,
                        tokenArgument,
                        cancellationTokenNoneArgument,
                        cancellationToken),
                    $"{nameof(HCR064_PassCancellationTokenCodeFixProvider)}.{cancellationTokenSymbol.Name}"),
                diagnostic);
        }
    }

    private static async Task<Document> PassCancellationTokenAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        ArgumentSyntax tokenArgument,
        ArgumentSyntax? cancellationTokenNoneArgument,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var updatedArgumentList = cancellationTokenNoneArgument is null
            ? invocation.ArgumentList.AddArguments(tokenArgument)
            : invocation.ArgumentList.ReplaceNode(
                cancellationTokenNoneArgument,
                tokenArgument.WithTriviaFrom(cancellationTokenNoneArgument));
        var updatedInvocation = invocation
            .WithArgumentList(updatedArgumentList)
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

    private static bool IsCancellationTokenSource(ITypeSymbol? type)
    {
        return type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::System.Threading.CancellationTokenSource";
    }

    private static bool IsCancellationTokenNone(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (expression is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "None"
            } memberAccess)
        {
            return false;
        }

        if (semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol is IPropertySymbol property)
        {
            return IsCancellationToken(property.ContainingType) && IsCancellationToken(property.Type);
        }

        return memberAccess.Expression.ToString().EndsWith("CancellationToken", System.StringComparison.Ordinal);
    }
}
