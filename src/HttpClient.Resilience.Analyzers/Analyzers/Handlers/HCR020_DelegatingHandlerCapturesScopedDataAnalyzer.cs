using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Models;
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
        context.RegisterCompilationStartAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationStartAnalysisContext context)
    {
        var scopedTypes = GetKnownScopedTypes(context.Compilation, context.CancellationToken);

        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeClass(nodeContext, scopedTypes),
            SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeClass(SyntaxNodeAnalysisContext context, ISet<string> scopedTypes)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        if (!DerivesFromDelegatingHandler(classDeclaration))
        {
            return;
        }

        foreach (var parameter in GetConstructorParameters(classDeclaration))
        {
            if (parameter.Type is null || !IsRequestScopedType(parameter.Type, scopedTypes))
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

    private static ISet<string> GetKnownScopedTypes(Compilation compilation, CancellationToken cancellationToken)
    {
        return new HashSet<string>(
            compilation.SyntaxTrees
                .Select(tree => tree.GetRoot(cancellationToken))
                .SelectMany(ServiceRegistrationCollector.Collect)
                .Where(registration => registration.Kind == ServiceRegistrationKind.Scoped)
                .SelectMany(registration => new[]
                {
                    registration.ServiceTypeName,
                    registration.ImplementationTypeName
                })
                .Where(typeName => typeName is not null)
                .SelectMany(typeName => TypeNameUtilities.GetComparableNames(typeName!)),
            System.StringComparer.Ordinal);
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

    private static bool IsRequestScopedType(TypeSyntax type, ISet<string> scopedTypes)
    {
        type = UnwrapNullableType(type);

        var simpleTypeName = GetSimpleTypeName(type);
        if (RequestScopedTypeNames.Contains(simpleTypeName, System.StringComparer.Ordinal))
        {
            return true;
        }

        if (TypeIsQualified(type))
        {
            return scopedTypes.Contains(type.ToString());
        }

        return TypeNameUtilities.GetComparableNames(simpleTypeName)
            .Any(scopedTypes.Contains);
    }

    private static TypeSyntax UnwrapNullableType(TypeSyntax type)
    {
        return type is NullableTypeSyntax nullable
            ? nullable.ElementType
            : type;
    }

    private static string GetSimpleTypeName(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText,
            _ => type.ToString()
        };
    }

    private static bool TypeIsQualified(TypeSyntax type)
    {
        return type is QualifiedNameSyntax or AliasQualifiedNameSyntax;
    }
}
