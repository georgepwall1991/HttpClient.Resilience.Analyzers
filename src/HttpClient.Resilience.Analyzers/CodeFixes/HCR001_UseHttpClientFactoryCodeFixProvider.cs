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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HCR001_UseHttpClientFactoryCodeFixProvider))]
[Shared]
public sealed class HCR001_UseHttpClientFactoryCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DiagnosticIds.HCR001);

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
        var creation = node as BaseObjectCreationExpressionSyntax ??
            node.FirstAncestorOrSelf<BaseObjectCreationExpressionSyntax>();
        if (creation is null)
        {
            return;
        }

        var factoryName = FindFactoryParameterName(creation);
        if (factoryName is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Create client with IHttpClientFactory",
                cancellationToken => UseFactoryAsync(context.Document, creation, factoryName, cancellationToken),
                nameof(HCR001_UseHttpClientFactoryCodeFixProvider)),
            diagnostic);
    }

    private static string? FindFactoryParameterName(SyntaxNode node)
    {
        if (node.FirstAncestorOrSelf<MethodDeclarationSyntax>() is { } method)
        {
            return FindFactoryParameterName(method.ParameterList.Parameters);
        }

        if (node.FirstAncestorOrSelf<LocalFunctionStatementSyntax>() is { } localFunction)
        {
            return FindFactoryParameterName(localFunction.ParameterList.Parameters);
        }

        return null;
    }

    private static string? FindFactoryParameterName(SeparatedSyntaxList<ParameterSyntax> parameters)
    {
        return parameters
            .Where(IsHttpClientFactoryParameter)
            .Select(parameter => parameter.Identifier.ValueText)
            .FirstOrDefault();
    }

    private static bool IsHttpClientFactoryParameter(ParameterSyntax parameter)
    {
        var type = parameter.Type?.ToString();
        return type == "IHttpClientFactory" ||
            (type?.EndsWith(".IHttpClientFactory", System.StringComparison.Ordinal) ?? false);
    }

    private static async Task<Document> UseFactoryAsync(
        Document document,
        BaseObjectCreationExpressionSyntax creation,
        string factoryName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var replacement = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(factoryName),
                    SyntaxFactory.IdentifierName("CreateClient")))
            .WithTriviaFrom(creation)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(creation, replacement));
    }
}
