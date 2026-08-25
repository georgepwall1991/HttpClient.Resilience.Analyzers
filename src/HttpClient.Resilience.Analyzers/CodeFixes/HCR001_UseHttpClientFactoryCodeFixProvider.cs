using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.KnownSymbols;
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
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root is null || semanticModel is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan);
            var creation = node as BaseObjectCreationExpressionSyntax ??
                node.FirstAncestorOrSelf<BaseObjectCreationExpressionSyntax>();
            if (creation is null)
            {
                continue;
            }

            var factoryName = FindFactoryParameterName(creation) ??
                FindFactoryMemberName(creation, semanticModel, context.CancellationToken);
            if (factoryName is null)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Create client with IHttpClientFactory",
                    cancellationToken => UseFactoryAsync(context.Document, creation, factoryName, cancellationToken),
                    nameof(HCR001_UseHttpClientFactoryCodeFixProvider)),
                diagnostic);
        }
    }

    private static string? FindFactoryParameterName(SyntaxNode node)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
        {
            if (ancestor is LocalFunctionStatementSyntax localFunction &&
                FindFactoryParameterName(localFunction.ParameterList.Parameters) is { } localFactoryName)
            {
                return localFactoryName;
            }

            if (ancestor is MethodDeclarationSyntax method &&
                FindFactoryParameterName(method.ParameterList.Parameters) is { } methodFactoryName)
            {
                return methodFactoryName;
            }

            if (ancestor is ClassDeclarationSyntax classDeclaration &&
                classDeclaration.ParameterList is { } parameterList &&
                FindFactoryParameterName(parameterList.Parameters) is { } classFactoryName)
            {
                return classFactoryName;
            }
        }

        return null;
    }

    private static string? FindFactoryMemberName(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var containingType = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (containingType is null)
        {
            return null;
        }

        var typeSymbol = semanticModel.GetDeclaredSymbol(containingType, cancellationToken);
        if (typeSymbol is null)
        {
            return null;
        }

        var factoryMembers = typeSymbol.GetMembers()
            .Where(member => member is IFieldSymbol field && HttpClientSymbols.IsHttpClientFactory(field.Type) ||
                member is IPropertySymbol property && HttpClientSymbols.IsHttpClientFactory(property.Type))
                .OrderBy(member => member.Name.IndexOf("Factory", System.StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1)
            .ToList();

        return factoryMembers.Count == 0 ? null : factoryMembers[0].Name;
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
