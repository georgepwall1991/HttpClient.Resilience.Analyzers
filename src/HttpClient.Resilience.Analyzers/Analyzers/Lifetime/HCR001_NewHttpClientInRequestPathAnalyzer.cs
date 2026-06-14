using System.Collections.Immutable;
using System.Linq;
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

    private static readonly string[] TestAttributeNames =
    {
        "Fact",
        "Theory",
        "Test",
        "TestCase",
        "TestCaseSource",
        "TestClass",
        "TestMethod",
        "DataTestMethod"
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

        if (!IsInExecutableCodeContext(creation))
        {
            return;
        }

        if (IsInTestContext(creation))
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

    private static bool IsInExecutableCodeContext(SyntaxNode node)
    {
        return node.FirstAncestorOrSelf<MethodDeclarationSyntax>() is not null ||
            node.FirstAncestorOrSelf<LocalFunctionStatementSyntax>() is not null ||
            node.FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>() is not null ||
            node.FirstAncestorOrSelf<GlobalStatementSyntax>() is not null;
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
            node.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>()?.UsingKeyword.IsKind(SyntaxKind.UsingKeyword) == true;
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

    private static bool IsInTestContext(SyntaxNode node)
    {
        var type = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (type is not null &&
            (IsTestTypeName(type.Identifier.ValueText) || HasTestAttribute(type.AttributeLists)))
        {
            return true;
        }

        return node.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>() is { } method &&
            HasTestAttribute(method.AttributeLists);
    }

    private static bool IsTestTypeName(string name)
    {
        return name.EndsWith("Test", System.StringComparison.Ordinal) ||
            name.EndsWith("Tests", System.StringComparison.Ordinal);
    }

    private static bool HasTestAttribute(SyntaxList<AttributeListSyntax> attributeLists)
    {
        return attributeLists
            .SelectMany(attributeList => attributeList.Attributes)
            .Any(attribute => IsTestAttributeName(attribute.Name));
    }

    private static bool IsTestAttributeName(NameSyntax name)
    {
        var text = name switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText,
            _ => name.ToString()
        };

        if (text.EndsWith("Attribute", System.StringComparison.Ordinal))
        {
            text = text.Substring(0, text.Length - "Attribute".Length);
        }

        return TestAttributeNames.Contains(text, System.StringComparer.Ordinal);
    }
}
