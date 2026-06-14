using System.Collections.Immutable;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.KnownSymbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.Lifetime;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR001_NewHttpClientInRequestPathAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] RequestPathTypeSuffixes =
    {
        "Controller",
        "Endpoint",
        "Handler",
        "Worker",
        "Service",
        "Repository",
        "Job"
    };

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

        if (creation.FirstAncestorOrSelf<MethodDeclarationSyntax>() is null &&
            creation.FirstAncestorOrSelf<LocalFunctionStatementSyntax>() is null &&
            creation.FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>() is null)
        {
            return;
        }

        if (IsInTestType(creation))
        {
            return;
        }

        if (!HasHighConfidenceRequestPathEvidence(creation))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR001,
            creation.GetLocation()));
    }

    private static bool HasHighConfidenceRequestPathEvidence(SyntaxNode node)
    {
        return IsInsideLoop(node) ||
            IsDisposedInUsing(node) ||
            IsInsideLikelyRequestPathType(node);
    }

    private static bool IsHttpClientCreation(
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (HttpClientSymbols.IsHttpClient(semanticModel.GetTypeInfo(creation, cancellationToken).Type))
        {
            return true;
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
            node.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>()?.UsingKeyword != default;
    }

    private static bool IsInsideLikelyRequestPathType(SyntaxNode node)
    {
        var type = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (type is null)
        {
            return false;
        }

        foreach (var suffix in RequestPathTypeSuffixes)
        {
            if (type.Identifier.ValueText.EndsWith(suffix, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInTestType(SyntaxNode node)
    {
        var type = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (type is null)
        {
            return false;
        }

        var name = type.Identifier.ValueText;
        return name.EndsWith("Test", System.StringComparison.Ordinal) ||
            name.EndsWith("Tests", System.StringComparison.Ordinal);
    }
}
