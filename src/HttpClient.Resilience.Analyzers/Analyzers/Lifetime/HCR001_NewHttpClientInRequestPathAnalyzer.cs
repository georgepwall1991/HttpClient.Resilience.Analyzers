using System.Collections.Immutable;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.KnownSymbols;
using HttpClient.Resilience.Analyzers.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.Lifetime;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR001_NewHttpClientInRequestPathAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR001);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression, SyntaxKind.ImplicitObjectCreationExpression);
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (BaseObjectCreationExpressionSyntax)context.Node;
        if (!IsHttpClientCreation(creation, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        if (!RequestPathContext.IsInExecutableCodeContext(creation))
        {
            return;
        }

        if (RequestPathContext.IsInTestContext(creation))
        {
            return;
        }

        if (!HasHighConfidenceRequestPathEvidence(
                creation,
                context.SemanticModel,
                context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR001,
            creation.GetLocation()));
    }

    private static bool HasHighConfidenceRequestPathEvidence(
        SyntaxNode node,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return IsInsideLoop(node) ||
            IsDisposedInUsing(node) ||
            RequestPathContext.IsInsideMinimalApiEndpoint(node, semanticModel, cancellationToken) ||
            RequestPathContext.IsInsideLikelyRequestPathType(node);
    }

    private static bool IsHttpClientCreation(
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var createdType = semanticModel.GetTypeInfo(creation, cancellationToken).Type;
        if (createdType is not null && createdType is not IErrorTypeSymbol)
        {
            return HttpClientSymbols.IsHttpClient(createdType);
        }

        return creation is ObjectCreationExpressionSyntax objectCreation &&
            HttpClientSymbols.IsHttpClientName(objectCreation.Type);
    }

    private static bool IsInsideLoop(SyntaxNode node)
    {
        return node.FirstAncestorOrSelf<ForStatementSyntax>() is not null ||
            node.FirstAncestorOrSelf<ForEachStatementSyntax>() is not null ||
            node.FirstAncestorOrSelf<WhileStatementSyntax>() is not null ||
            node.FirstAncestorOrSelf<DoStatementSyntax>() is not null;
    }

    private static bool IsDisposedInUsing(SyntaxNode node)
    {
        return node.FirstAncestorOrSelf<UsingStatementSyntax>() is not null ||
            node.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>()?.UsingKeyword.IsKind(SyntaxKind.UsingKeyword) == true;
    }
}
