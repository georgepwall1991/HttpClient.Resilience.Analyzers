using System.Collections.Immutable;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.Resilience;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR082_PerRequestResiliencePipelineAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR082);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name.Identifier.ValueText != "Build" ||
            !IsResiliencePipelineBuildMethod(invocation, context.SemanticModel, context.CancellationToken) ||
            !ReceiverLooksLikeResiliencePipelineBuilder(
                memberAccess.Expression,
                context.SemanticModel,
                context.CancellationToken) ||
            !RequestPathContext.IsInExecutableCodeContext(invocation) ||
            RequestPathContext.IsInTestContext(invocation) ||
            !HasHighConfidenceRequestPathEvidence(invocation, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR082,
            memberAccess.Name.GetLocation()));
    }

    private static bool IsResiliencePipelineBuildMethod(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return IsResiliencePipelineBuilderMethod(method);
        }

        var candidateMethods = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
        return candidateMethods.Length == 0
            ? invocation.ArgumentList.Arguments.Count == 0
            : candidateMethods.All(IsResiliencePipelineBuilderMethod);
    }

    private static bool IsResiliencePipelineBuilderMethod(IMethodSymbol method)
    {
        var declaringType = (method.ReducedFrom ?? method).ContainingType;
        return declaringType.Name == "ResiliencePipelineBuilder" &&
            declaringType.ContainingNamespace.ToDisplayString() == "Polly";
    }

    private static bool ReceiverLooksLikeResiliencePipelineBuilder(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        expression = SyntaxTransparency.Unwrap(expression);

        var expressionType = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        if (expressionType is not null && expressionType is not IErrorTypeSymbol)
        {
            return IsResiliencePipelineBuilderType(expressionType);
        }

        return expression switch
        {
            ObjectCreationExpressionSyntax objectCreation => IsResiliencePipelineBuilderCreation(
                objectCreation,
                semanticModel,
                cancellationToken),
            ImplicitObjectCreationExpressionSyntax implicitObjectCreation => IsResiliencePipelineBuilderCreation(
                implicitObjectCreation,
                semanticModel,
                cancellationToken),
            IdentifierNameSyntax identifier => VisibleIdentifierLooksLikeResiliencePipelineBuilder(
                identifier,
                semanticModel,
                cancellationToken),
            MemberAccessExpressionSyntax memberAccess => ReceiverLooksLikeResiliencePipelineBuilder(
                memberAccess.Expression,
                semanticModel,
                cancellationToken),
            InvocationExpressionSyntax invocation => invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                ReceiverLooksLikeResiliencePipelineBuilder(
                    memberAccess.Expression,
                    semanticModel,
                    cancellationToken),
            _ => false
        };
    }

    private static bool IsResiliencePipelineBuilderCreation(
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var createdType = semanticModel.GetTypeInfo(creation, cancellationToken).Type;
        if (createdType is not null && createdType is not IErrorTypeSymbol)
        {
            return IsResiliencePipelineBuilderType(createdType);
        }

        return creation is ObjectCreationExpressionSyntax objectCreation &&
            IsResiliencePipelineBuilderTypeName(objectCreation.Type);
    }

    private static bool VisibleIdentifierLooksLikeResiliencePipelineBuilder(
        IdentifierNameSyntax identifier,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolType = semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null
        };

        if (symbolType is not null && symbolType is not IErrorTypeSymbol)
        {
            return IsResiliencePipelineBuilderType(symbolType);
        }

        return RequestPathContext.VisibleIdentifierDeclarationType(identifier) is { } type &&
            IsResiliencePipelineBuilderTypeName(type) ||
            VisibleIdentifierInitializerLooksLikeResiliencePipelineBuilder(
                identifier,
                semanticModel,
                cancellationToken);
    }

    private static bool VisibleIdentifierInitializerLooksLikeResiliencePipelineBuilder(
        IdentifierNameSyntax identifier,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return identifier.FirstAncestorOrSelf<BlockSyntax>()?
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText &&
                variable.SpanStart < identifier.SpanStart &&
                variable.Initializer is not null &&
                !SyntaxTransparency.LocalIsReassignedBetween(
                    variable.FirstAncestorOrSelf<BlockSyntax>()!,
                    identifier.Identifier.ValueText,
                    variable.SpanStart,
                    identifier.SpanStart))
            .Select(variable => variable.Initializer!.Value)
            .Any(initializer => ReceiverLooksLikeResiliencePipelineBuilder(
                initializer,
                semanticModel,
                cancellationToken)) == true;
    }

    private static bool IsResiliencePipelineBuilderType(ITypeSymbol type)
    {
        return type is INamedTypeSymbol namedType &&
            namedType.Name == "ResiliencePipelineBuilder" &&
            namedType.ContainingNamespace.ToDisplayString() == "Polly";
    }

    private static bool IsResiliencePipelineBuilderTypeName(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "ResiliencePipelineBuilder",
            GenericNameSyntax generic => generic.Identifier.ValueText == "ResiliencePipelineBuilder",
            QualifiedNameSyntax qualified => qualified.ToString() is
                "Polly.ResiliencePipelineBuilder" or
                "global::Polly.ResiliencePipelineBuilder" ||
                qualified.Right is GenericNameSyntax { Identifier.ValueText: "ResiliencePipelineBuilder" } &&
                qualified.Left.ToString() is "Polly" or "global::Polly",
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.ToString().StartsWith(
                "global::Polly.ResiliencePipelineBuilder",
                System.StringComparison.Ordinal),
            _ => false
        };
    }

    private static bool HasHighConfidenceRequestPathEvidence(
        SyntaxNode node,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return RequestPathContext.IsInsideMinimalApiEndpoint(node, semanticModel, cancellationToken) ||
            RequestPathContext.IsInsideLikelyRequestPathType(node);
    }

}
