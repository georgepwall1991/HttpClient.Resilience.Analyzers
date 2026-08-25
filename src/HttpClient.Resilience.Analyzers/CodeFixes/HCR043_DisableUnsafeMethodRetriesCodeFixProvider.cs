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
            if (invocation is null || semanticModel is null)
            {
                continue;
            }

            // The statement-based insertion requires a block; expression-bodied lambdas are
            // handled by converting the whole lambda body.
            var supportsStatementInsertion =
                invocation.FirstAncestorOrSelf<StatementSyntax>()?.Parent is BlockSyntax ||
                invocation.FirstAncestorOrSelf<LambdaExpressionSyntax>()?.Body is ExpressionSyntax;
            if (!supportsStatementInsertion)
            {
                continue;
            }

            var hasInlineCreation = TryGetHttpRetryStrategyOptionsCreation(
                invocation,
                semanticModel,
                context.CancellationToken,
                out _);
            var hasOptionsVariable = !hasInlineCreation &&
                TryGetHttpRetryStrategyOptionsVariable(
                    invocation,
                    semanticModel,
                    context.CancellationToken,
                    out _);

            if (!hasInlineCreation && !hasOptionsVariable)
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
        if (root is null || semanticModel is null)
        {
            return document;
        }

        var hasInlineCreation = TryGetHttpRetryStrategyOptionsCreation(
            invocation,
            semanticModel,
            cancellationToken,
            out var objectCreation);
        IdentifierNameSyntax? optionsIdentifier = null;
        var hasOptionsVariable = !hasInlineCreation &&
            TryGetHttpRetryStrategyOptionsVariable(
                invocation,
                semanticModel,
                cancellationToken,
                out optionsIdentifier);

        if (!hasInlineCreation && !hasOptionsVariable)
        {
            return document;
        }

        var optionsName = hasInlineCreation
            ? ChooseRetryOptionsName(invocation)
            : optionsIdentifier!.Identifier.ValueText;
        var declaration = hasInlineCreation ? CreateOptionsDeclaration(optionsName, objectCreation) : null;
        var disableCall = CreateDisableCall(optionsName);
        var identifier = SyntaxFactory.IdentifierName(optionsName);

        var lambda = invocation.FirstAncestorOrSelf<LambdaExpressionSyntax>();
        if (hasInlineCreation && lambda?.Body is ExpressionSyntax expressionBody)
        {
            var rewrittenExpression = expressionBody.ReplaceNode(objectCreation, identifier);
            var block = SyntaxFactory.Block(declaration!, disableCall, SyntaxFactory.ExpressionStatement(rewrittenExpression));
            var rewrittenRoot = EnsureResilienceImport(
                root.ReplaceNode(
                    lambda,
                    lambda.WithBody(block).WithAdditionalAnnotations(Formatter.Annotation)),
                semanticModel.Compilation);
            return document.WithSyntaxRoot(rewrittenRoot);
        }

        var statement = invocation.FirstAncestorOrSelf<StatementSyntax>();
        if (statement?.Parent is not BlockSyntax)
        {
            return document;
        }

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        if (hasInlineCreation)
        {
            editor.InsertBefore(statement, new SyntaxNode[] { declaration!, disableCall });
            editor.ReplaceNode(objectCreation, identifier);
        }
        else
        {
            editor.InsertBefore(statement, disableCall);
        }

        var changedDocument = editor.GetChangedDocument();
        var changedRoot = await changedDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (changedRoot is null)
        {
            return changedDocument;
        }

        return document.WithSyntaxRoot(
            EnsureResilienceImport(changedRoot, semanticModel.Compilation));
    }

    private const string ResilienceNamespace = "Microsoft.Extensions.Http.Resilience";

    private static SyntaxNode EnsureResilienceImport(SyntaxNode root, Compilation compilation)
    {
        // The generated DisableForUnsafeHttpMethods() call is an extension method from the
        // resilience namespace; make sure that namespace is imported when it exists in the
        // compilation (test stubs define a global-namespace stand-in instead).
        if (compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.Http.Resilience.HttpRetryStrategyOptionsExtensions") is null ||
            root is not CompilationUnitSyntax compilationUnit)
        {
            return root;
        }

        var alreadyImported = compilationUnit.Usings.Any(usingDirective =>
            string.Equals(
                usingDirective.Name?.ToFullString().Trim(),
                ResilienceNamespace,
                System.StringComparison.Ordinal) ||
            string.Equals(
                usingDirective.Name?.ToFullString().Trim(),
                "global::" + ResilienceNamespace,
                System.StringComparison.Ordinal));
        if (alreadyImported)
        {
            return root;
        }

        return compilationUnit.WithUsings(
            compilationUnit.Usings.Insert(
                0,
                SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(ResilienceNamespace))
                    .WithAdditionalAnnotations(Formatter.Annotation)));
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
        if (!IsHttpRetryStrategyOptionsType(type))
        {
            return false;
        }
        objectCreation = creation;
        return true;
    }

    private static bool TryGetHttpRetryStrategyOptionsVariable(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IdentifierNameSyntax? optionsIdentifier)
    {
        optionsIdentifier = null;
        if (!ResilienceRetryInvocation.IsFrameworkAddRetry(invocation, semanticModel, cancellationToken) ||
            invocation.ArgumentList.Arguments.Count != 1)
        {
            return false;
        }

        var argument = SyntaxTransparency.Unwrap(invocation.ArgumentList.Arguments[0].Expression);
        if (argument is not IdentifierNameSyntax identifier)
        {
            return false;
        }

        if (semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol is not ILocalSymbol local ||
            !IsHttpRetryStrategyOptionsType(local.Type))
        {
            return false;
        }

        // The inserted guard call must live in the same block, after the declaration that
        // makes the options variable visible; otherwise the fix would not compile or would
        // not affect the flagged pipeline.
        var statement = invocation.FirstAncestorOrSelf<StatementSyntax>();
        if (statement?.Parent is not BlockSyntax block)
        {
            return false;
        }

        if (!block.ChildNodes().OfType<LocalDeclarationStatementSyntax>()
                .Any(candidate =>
                    candidate.SpanStart < invocation.SpanStart &&
                    semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol is ILocalSymbol declared &&
                    DeclaresLocal(candidate, declared)))
        {
            return false;
        }
        // Any earlier method call on the same options variable may already be a guard we do
        // not recognize; adding ours blindly could double-guard or mask a custom strategy.
        if (block.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Any(guardInvocation => guardInvocation.SpanStart < invocation.SpanStart &&
                    guardInvocation.Expression is MemberAccessExpressionSyntax
                    {
                        Expression: IdentifierNameSyntax receiver
                    } &&
                    receiver.Identifier.ValueText == identifier.Identifier.ValueText))
        {
            return false;
        }

        optionsIdentifier = identifier;
        return true;
    }

    private static bool DeclaresLocal(LocalDeclarationStatementSyntax declaration, ILocalSymbol local)
    {
        return declaration.Declaration.Variables
            .Any(variable => string.Equals(variable.Identifier.ValueText, local.Name, System.StringComparison.Ordinal));
    }

    private static bool IsHttpRetryStrategyOptionsType(ITypeSymbol? type)
    {
        if (type is null || type.Name != "HttpRetryStrategyOptions")
        {
            return false;
        }

        var containingNamespace = type.ContainingNamespace;
        return containingNamespace.IsGlobalNamespace ||
            containingNamespace.ToDisplayString() == "Microsoft.Extensions.Http.Resilience";
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
