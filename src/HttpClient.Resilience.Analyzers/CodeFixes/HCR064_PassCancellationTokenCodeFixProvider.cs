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
using Microsoft.CodeAnalysis.Operations;

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

        var nonCancelableTokenArgument = invocation.ArgumentList.Arguments
            .FirstOrDefault(argument =>
                ArgumentTargetsCancellationToken(invocation, argument, semanticModel, context.CancellationToken) &&
                IsNonCancelableTokenExpression(argument.Expression, semanticModel, context.CancellationToken));

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
                        nonCancelableTokenArgument,
                        cancellationToken),
                    $"{nameof(HCR064_PassCancellationTokenCodeFixProvider)}.{cancellationTokenSymbol.Name}"),
                diagnostic);
        }
    }

    private static async Task<Document> PassCancellationTokenAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        ArgumentSyntax tokenArgument,
        ArgumentSyntax? nonCancelableTokenArgument,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var updatedArgumentList = nonCancelableTokenArgument is null
            ? invocation.ArgumentList.AddArguments(tokenArgument)
            : invocation.ArgumentList.ReplaceNode(
                nonCancelableTokenArgument,
                tokenArgument.WithTriviaFrom(nonCancelableTokenArgument));
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

    private static bool ArgumentTargetsCancellationToken(
        InvocationExpressionSyntax invocation,
        ArgumentSyntax argument,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (argument.NameColon?.Name.Identifier.ValueText == "cancellationToken")
        {
            return true;
        }

        if (semanticModel.GetOperation(argument, cancellationToken) is IArgumentOperation argumentOperation &&
            argumentOperation.Parameter is { } operationParameter)
        {
            return IsCancellationToken(operationParameter.Type);
        }

        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        var candidateMethods = symbolInfo.Symbol is IMethodSymbol resolvedMethod
            ? new[] { resolvedMethod }.AsEnumerable()
            : symbolInfo.CandidateSymbols.OfType<IMethodSymbol>();
        foreach (var method in candidateMethods)
        {
            if (GetParameterForArgument(method, invocation, argument) is { } parameter &&
                IsCancellationToken(parameter.Type))
            {
                return true;
            }
        }

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            foreach (var method in semanticModel.GetMemberGroup(memberAccess, cancellationToken).OfType<IMethodSymbol>())
            {
                if (GetParameterForArgument(method, invocation, argument) is { } parameter &&
                    IsCancellationToken(parameter.Type))
                {
                    return true;
                }
            }

            if (semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type is { } receiverType)
            {
                foreach (var method in receiverType
                    .GetMembers(memberAccess.Name.Identifier.ValueText)
                    .OfType<IMethodSymbol>())
                {
                    if (GetParameterForArgument(method, invocation, argument) is { } parameter &&
                        IsCancellationToken(parameter.Type))
                    {
                        return true;
                    }
                }
            }
        }

        if (argument.Expression is DefaultExpressionSyntax defaultExpression &&
            defaultExpression.Type is IdentifierNameSyntax
            {
                Identifier.ValueText: "CancellationToken"
            })
        {
            return true;
        }

        var typeInfo = semanticModel.GetTypeInfo(argument.Expression, cancellationToken);
        return IsCancellationToken(typeInfo.Type ?? typeInfo.ConvertedType);
    }

    private static IParameterSymbol? GetParameterForArgument(
        IMethodSymbol method,
        InvocationExpressionSyntax invocation,
        ArgumentSyntax argument)
    {
        if (argument.NameColon is { } nameColon)
        {
            return method.Parameters.FirstOrDefault(candidate =>
                candidate.Name == nameColon.Name.Identifier.ValueText);
        }

        var argumentIndex = invocation.ArgumentList.Arguments.IndexOf(argument);
        return argumentIndex >= 0 && argumentIndex < method.Parameters.Length
            ? method.Parameters[argumentIndex]
            : null;
    }

    private static bool IsCancellationTokenSource(ITypeSymbol? type)
    {
        return type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::System.Threading.CancellationTokenSource";
    }

    private static bool IsNonCancelableTokenExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax postfix
                    when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = postfix.Operand;
                    continue;
                case CastExpressionSyntax cast:
                    expression = cast.Expression;
                    continue;
                default:
                    break;
            }

            break;
        }

        if (expression.IsKind(SyntaxKind.DefaultLiteralExpression) || expression is DefaultExpressionSyntax)
        {
            return true;
        }

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
