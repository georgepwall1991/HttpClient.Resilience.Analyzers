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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HCR002_AddPooledConnectionLifetimeCodeFixProvider))]
[Shared]
public sealed class HCR002_AddPooledConnectionLifetimeCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DiagnosticIds.HCR002);

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

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan);

            var variable = node.FirstAncestorOrSelf<VariableDeclaratorSyntax>();
            if (variable?.Initializer is not null &&
                CanSafelyConfigureInitializer(variable.Initializer.Value))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Configure PooledConnectionLifetime",
                        cancellationToken => ReplaceInitializerValueAsync(
                            context.Document,
                            variable.Initializer,
                            cancellationToken),
                        nameof(HCR002_AddPooledConnectionLifetimeCodeFixProvider)),
                    diagnostic);
                continue;
            }

            if (variable?.Initializer?.Value is BaseObjectCreationExpressionSyntax variableCreation &&
                TryGetInlineUnconfiguredHandler(
                    variableCreation,
                    semanticModel,
                    context.CancellationToken,
                    out var variableHandler))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Configure PooledConnectionLifetime",
                        cancellationToken => ConfigureInlineHandlerAsync(
                            context.Document,
                            variableHandler,
                            cancellationToken),
                        nameof(HCR002_AddPooledConnectionLifetimeCodeFixProvider)),
                    diagnostic);
                continue;
            }

            var property = node.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
            if (property?.Initializer is not null &&
                CanSafelyConfigureInitializer(property.Initializer.Value))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Configure PooledConnectionLifetime",
                        cancellationToken => ReplaceInitializerValueAsync(
                            context.Document,
                            property.Initializer,
                            cancellationToken),
                        nameof(HCR002_AddPooledConnectionLifetimeCodeFixProvider)),
                    diagnostic);
                continue;
            }

            if (property?.Initializer?.Value is BaseObjectCreationExpressionSyntax propertyCreation &&
                TryGetInlineUnconfiguredHandler(
                    propertyCreation,
                    semanticModel,
                    context.CancellationToken,
                    out var propertyHandler))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Configure PooledConnectionLifetime",
                        cancellationToken => ConfigureInlineHandlerAsync(
                            context.Document,
                            propertyHandler,
                            cancellationToken),
                        nameof(HCR002_AddPooledConnectionLifetimeCodeFixProvider)),
                    diagnostic);
            }
        }
    }

    private static bool TryGetInlineUnconfiguredHandler(
        BaseObjectCreationExpressionSyntax clientCreation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out BaseObjectCreationExpressionSyntax handlerCreation)
    {
        handlerCreation = null!;

        // A second argument is always the disposeHandler flag, which the merge preserves
        // untouched, so only the handler argument needs verification. The handler is the
        // first positional argument or the argument explicitly named "handler".
        if (clientCreation.ArgumentList?.Arguments.Count is not 1 and not 2 ||
            !TryGetHandlerArgument(clientCreation, out var handlerExpression) ||
            UnwrapTransparentExpression(handlerExpression) is not BaseObjectCreationExpressionSyntax candidate ||
            (candidate.ArgumentList?.Arguments.Count ?? 0) != 0)
        {
            return false;
        }

        if (semanticModel.GetTypeInfo(candidate, cancellationToken).Type is not { } handlerType ||
            !IsFrameworkSocketsHttpHandler(handlerType, semanticModel))
        {
            return false;
        }

        if (HasPooledConnectionLifetimeAssignment(candidate) ||
            HasDisallowedTrivia(candidate))
        {
            return false;
        }

        handlerCreation = candidate;
        return true;
    }

    private static bool TryGetHandlerArgument(
        BaseObjectCreationExpressionSyntax clientCreation,
        out ExpressionSyntax handlerExpression)
    {
        handlerExpression = null!;

        foreach (var argument in clientCreation.ArgumentList?.Arguments ?? default(SeparatedSyntaxList<ArgumentSyntax>))
        {
            if (argument.NameColon is null)
            {
                // Positional arguments precede named ones, so the first positional
                // argument targets the handler parameter.
                handlerExpression = argument.Expression;
                return true;
            }

            if (argument.NameColon.Name.Identifier.ValueText == "handler")
            {
                handlerExpression = argument.Expression;
                return true;
            }
        }

        return false;
    }

    private static ExpressionSyntax UnwrapTransparentExpression(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax postfix when
                    postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = postfix.Operand;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private static bool HasPooledConnectionLifetimeAssignment(BaseObjectCreationExpressionSyntax handlerCreation)
    {
        return handlerCreation.Initializer?.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.Left is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == "PooledConnectionLifetime") == true;
    }

    private static bool IsFrameworkSocketsHttpHandler(ITypeSymbol handlerType, SemanticModel semanticModel)
    {
        // Namespace comparison alone would accept a source-defined impersonator in the
        // System.Net.Http namespace, for which GetTypeByMetadataName can even return the
        // impersonator itself, so require a metadata-defined framework type identity.
        return handlerType.Locations.All(static location => location.IsInMetadata) &&
            semanticModel.Compilation.GetTypeByMetadataName("System.Net.Http.SocketsHttpHandler") is { } frameworkType &&
            SymbolEqualityComparer.Default.Equals(handlerType.OriginalDefinition, frameworkType);
    }

    private static bool HasDisallowedTrivia(SyntaxNode node)
    {
        return node.DescendantTrivia().Any(trivia => trivia.IsDirective ||
            trivia.Kind() is
                SyntaxKind.SingleLineCommentTrivia or
                SyntaxKind.MultiLineCommentTrivia or
                SyntaxKind.SingleLineDocumentationCommentTrivia or
                SyntaxKind.MultiLineDocumentationCommentTrivia or
                SyntaxKind.DisabledTextTrivia or
                SyntaxKind.SkippedTokensTrivia);
    }

    private static bool CanSafelyConfigureInitializer(ExpressionSyntax expression)
    {
        // Replacing the whole expression would drop any comment inside it, so withhold the
        // fix when the initializer carries significant internal trivia.
        return expression is BaseObjectCreationExpressionSyntax creation &&
            creation.ArgumentList?.Arguments.Count == 0 &&
            creation.Initializer is null &&
            !HasDisallowedTrivia(creation);
    }

    private static async Task<Document> ReplaceInitializerValueAsync(
        Document document,
        EqualsValueClauseSyntax initializer,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var replacement = SyntaxFactory.ParseExpression(
                "new global::System.Net.Http.HttpClient(new global::System.Net.Http.SocketsHttpHandler { PooledConnectionLifetime = global::System.TimeSpan.FromMinutes(2) })")
            .WithTriviaFrom(initializer.Value)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(
            root.ReplaceNode(initializer, initializer.WithValue(replacement)));
    }

    private static async Task<Document> ConfigureInlineHandlerAsync(
        Document document,
        BaseObjectCreationExpressionSyntax handlerCreation,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var lifetimeAssignment = SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxFactory.IdentifierName("PooledConnectionLifetime"),
            SyntaxFactory.ParseExpression("global::System.TimeSpan.FromMinutes(2)"));
        var initializer = handlerCreation.Initializer is { } existing
            ? existing.WithExpressions(existing.Expressions.Add(lifetimeAssignment))
            : SyntaxFactory.InitializerExpression(
                SyntaxKind.ObjectInitializerExpression,
                SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(lifetimeAssignment));
        var configuredHandler = handlerCreation
            .WithInitializer(initializer)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(handlerCreation, configuredHandler));
    }
}
