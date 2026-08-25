using System;
using System.Collections.Generic;
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
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;

namespace HttpClient.Resilience.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HCR043_DisableUnsafeMethodRetriesCodeFixProvider))]
[Shared]
public sealed class HCR043_DisableUnsafeMethodRetriesCodeFixProvider : CodeFixProvider
{
    public const string Title = "Disable retries for unsafe HTTP methods";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DiagnosticIds.HCR043);

    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        // Stryker disable once boolean: analyzer code fixes do not run on a captured SynchronizationContext
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(false);

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan);
            var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();

            if (invocation is null ||
                semanticModel is null ||
                !TryGetHttpRetryStrategyOptionsCreation(
                    invocation,
                    semanticModel,
                    context.CancellationToken,
                    out _))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    cancellationToken => DisableUnsafeMethodRetriesAsync(
                        context.Document,
                        invocation,
                        cancellationToken),
                    nameof(HCR043_DisableUnsafeMethodRetriesCodeFixProvider)),
                diagnostic);
        }
    }

    private static async Task<Document> DisableUnsafeMethodRetriesAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        // Stryker disable once boolean: analyzer code fixes do not run on a captured SynchronizationContext
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null ||
            semanticModel is null ||
            !TryGetHttpRetryStrategyOptionsCreation(
                invocation,
                semanticModel,
                cancellationToken,
                out var objectCreation))
        {
            return document;
        }

        var optionsName = ChooseRetryOptionsName(invocation);
        var declaration = CreateOptionsDeclaration(optionsName, objectCreation);
        var disableCall = CreateDisableCall(optionsName);
        var identifier = SyntaxFactory.IdentifierName(optionsName);

        var lambda = invocation.FirstAncestorOrSelf<LambdaExpressionSyntax>();
        if (lambda?.Body is ExpressionSyntax expressionBody)
        {
            var rewrittenExpression = expressionBody.ReplaceNode(objectCreation, identifier);
            var block = SyntaxFactory.Block(declaration, disableCall, SyntaxFactory.ExpressionStatement(rewrittenExpression));
            return document.WithSyntaxRoot(
                root.ReplaceNode(
                    lambda,
                    lambda.WithBody(block).WithAdditionalAnnotations(Formatter.Annotation)));
        }

        var statement = invocation.FirstAncestorOrSelf<StatementSyntax>();
        if (statement?.Parent is not BlockSyntax)
        {
            return document;
        }

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        editor.InsertBefore(statement, new SyntaxNode[] { declaration, disableCall });
        editor.ReplaceNode(objectCreation, identifier);
        return editor.GetChangedDocument();
    }

    private static bool TryGetHttpRetryStrategyOptionsCreation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ObjectCreationExpressionSyntax objectCreation)
    {
        objectCreation = null!;
        if (!ResilienceRetryInvocation.IsFrameworkAddRetry(invocation, semanticModel, cancellationToken) ||
            invocation.ArgumentList.Arguments.Count == 0)
        {
            return false;
        }

        var argument = SyntaxTransparency.Unwrap(invocation.ArgumentList.Arguments[0].Expression);
        if (argument is not ObjectCreationExpressionSyntax creation)
        {
            return false;
        }

        var type = semanticModel.GetTypeInfo(creation, cancellationToken).Type;
        if (type is null || type.Name != "HttpRetryStrategyOptions")
        {
            return false;
        }

        var containingNamespace = type.ContainingNamespace;
        if (!containingNamespace.IsGlobalNamespace &&
            containingNamespace.ToDisplayString() != "Microsoft.Extensions.Http.Resilience")
        {
            return false;
        }

        objectCreation = creation;
        return true;
    }

    private static string ChooseRetryOptionsName(InvocationExpressionSyntax invocation)
    {
        var scope = GetNameScope(invocation);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declarator in scope.DescendantNodesAndSelf().OfType<VariableDeclaratorSyntax>())
        {
            used.Add(declarator.Identifier.ValueText);
        }

        foreach (var parameter in scope.DescendantNodesAndSelf().OfType<ParameterSyntax>())
        {
            used.Add(parameter.Identifier.ValueText);
        }

        return PickRetryOptionsName(used, CountEarlierFixableAddRetryCreations(invocation, scope));
    }

    private static SyntaxNode GetNameScope(SyntaxNode node)
    {
        return (SyntaxNode?)node.FirstAncestorOrSelf<LocalFunctionStatementSyntax>()
            ?? node.FirstAncestorOrSelf<MethodDeclarationSyntax>()
            ?? (SyntaxNode?)node.FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>()
            ?? node.SyntaxTree.GetRoot();
    }

    private static int CountEarlierFixableAddRetryCreations(
        InvocationExpressionSyntax invocation,
        SyntaxNode scope)
    {
        var count = 0;
        foreach (var candidate in scope.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (candidate.SpanStart >= invocation.SpanStart)
            {
                continue;
            }

            if (IsFixableAddRetryObjectCreation(candidate))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsFixableAddRetryObjectCreation(InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList.Arguments.Count == 0 ||
            invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name.Identifier.ValueText != "AddRetry")
        {
            return false;
        }

        var argument = SyntaxTransparency.Unwrap(invocation.ArgumentList.Arguments[0].Expression);
        return argument is ObjectCreationExpressionSyntax creation &&
            GetCreatedTypeName(creation) == "HttpRetryStrategyOptions";
    }

    private static string? GetCreatedTypeName(ObjectCreationExpressionSyntax creation)
    {
        return creation.Type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax aliased => aliased.Name.Identifier.ValueText,
            _ => null
        };
    }

    private static string PickRetryOptionsName(HashSet<string> used, int occurrenceIndex)
    {
        var remaining = occurrenceIndex;
        foreach (var candidate in CandidateRetryOptionsNames())
        {
            if (used.Contains(candidate))
            {
                continue;
            }

            if (remaining == 0)
            {
                return candidate;
            }

            remaining--;
        }

        return "retryOptions";
    }

    private static IEnumerable<string> CandidateRetryOptionsNames()
    {
        yield return "retryOptions";
        for (var suffix = 2; ; suffix++)
        {
            yield return "retryOptions" + suffix;
        }
    }

    private static LocalDeclarationStatementSyntax CreateOptionsDeclaration(
        string optionsName,
        ObjectCreationExpressionSyntax objectCreation)
    {
        return SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName("var"))
                    .WithVariables(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(optionsName))
                                .WithInitializer(SyntaxFactory.EqualsValueClause(objectCreation.WithoutTrivia())))))
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    private static ExpressionStatementSyntax CreateDisableCall(string optionsName)
    {
        return SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName(optionsName),
                        SyntaxFactory.IdentifierName("DisableForUnsafeHttpMethods"))))
            .WithAdditionalAnnotations(Formatter.Annotation);
    }
}
