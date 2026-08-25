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

            // Static methods and static lambdas cannot reference instance members or captured
            // parameters, so neither fallback is valid there.
            var factoryName = !RequiresStaticContext(creation)
                ? FindFactoryParameterName(creation) ??
                    FindFactoryMemberName(creation, semanticModel, context.CancellationToken)
                : null;
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
        if (containingType is null || RequiresStaticContext(node))
        {
            return null;
        }

        var typeSymbol = semanticModel.GetDeclaredSymbol(containingType, cancellationToken);
        if (typeSymbol is null)
        {
            return null;
        }

        var factoryMembers = typeSymbol.GetMembers()
            .Where(member => !member.IsImplicitlyDeclared)
            .Select(member => member switch
            {
                IFieldSymbol field when IsUsableFactoryType(field.Type) => member,
                IPropertySymbol property when property.GetMethod is not null &&
                    IsUsableFactoryType(property.Type) => member,
                _ => null
            })
            .OfType<ISymbol>()
            .OrderBy(member => member.Name.IndexOf("Factory", System.StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1)
            .ThenBy(member => member.Name, System.StringComparer.Ordinal);

        foreach (var member in factoryMembers)
        {
            // A local or parameter with the same name would shadow the member and redirect
            // the fix at the wrong symbol, so only unshadowed members qualify.
            var lookup = semanticModel.LookupSymbols(node.SpanStart)
                .Where(symbol => symbol.Name == member.Name);
            if (lookup.All(symbol => SymbolEqualityComparer.Default.Equals(symbol, member)))
            {
                return member.Name;
            }
        }

        return null;
    }

    private static bool RequiresStaticContext(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is TypeDeclarationSyntax)
            {
                return false;
            }

            if (ancestor switch
            {
                MethodDeclarationSyntax method => HasStaticModifier(method.Modifiers),
                LocalFunctionStatementSyntax localFunction => HasStaticModifier(localFunction.Modifiers),
                SimpleLambdaExpressionSyntax simpleLambda => HasStaticModifier(simpleLambda.Modifiers),
                ParenthesizedLambdaExpressionSyntax parenthesizedLambda => HasStaticModifier(parenthesizedLambda.Modifiers),
                AnonymousMethodExpressionSyntax anonymousMethod => HasStaticModifier(anonymousMethod.Modifiers),
                _ => false
            })
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasStaticModifier(SyntaxTokenList modifiers)
    {
        return modifiers.Any(SyntaxKind.StaticKeyword);
    }
    private static bool IsUsableFactoryType(ITypeSymbol? type)
    {
        return type?.NullableAnnotation != Microsoft.CodeAnalysis.NullableAnnotation.Annotated &&
            type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) is
                "global::System.Net.Http.IHttpClientFactory" or
                "global::IHttpClientFactory";
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
