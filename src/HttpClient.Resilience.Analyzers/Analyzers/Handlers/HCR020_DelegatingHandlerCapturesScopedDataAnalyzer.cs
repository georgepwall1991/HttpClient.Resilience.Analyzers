using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.Handlers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR020_DelegatingHandlerCapturesScopedDataAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] RequestScopedTypeNames =
    {
        "IHttpContextAccessor",
        "HttpContext",
        "ClaimsPrincipal",
        "ISession"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR020);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeClass(SyntaxNodeAnalysisContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        if (!DerivesFromDelegatingHandler(classDeclaration))
        {
            return;
        }

        foreach (var parameter in GetConstructorParameters(classDeclaration))
        {
            if (parameter.Type is null || !IsRequestScopedType(parameter.Type))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.HCR020,
                parameter.Type.GetLocation()));
        }
    }

    private static bool DerivesFromDelegatingHandler(ClassDeclarationSyntax classDeclaration)
    {
        return classDeclaration.BaseList?.Types.Any(type =>
            type.Type switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "DelegatingHandler",
                QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText == "DelegatingHandler",
                _ => false
            }) == true;
    }

    private static IEnumerable<ParameterSyntax> GetConstructorParameters(ClassDeclarationSyntax classDeclaration)
    {
        foreach (var constructor in classDeclaration.Members.OfType<ConstructorDeclarationSyntax>())
        {
            foreach (var parameter in constructor.ParameterList.Parameters)
            {
                yield return parameter;
            }
        }

        if (classDeclaration.ParameterList is null)
        {
            yield break;
        }

        foreach (var parameter in classDeclaration.ParameterList.Parameters)
        {
            yield return parameter;
        }
    }

    private static bool IsRequestScopedType(TypeSyntax type)
    {
        var typeName = type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText,
            NullableTypeSyntax nullable => nullable.ElementType.ToString(),
            _ => type.ToString()
        };

        return RequestScopedTypeNames.Contains(typeName, System.StringComparer.Ordinal);
    }
}
