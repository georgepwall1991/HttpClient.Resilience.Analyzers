using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.TypedClients;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR004_TypedClientInjectedIntoSingletonAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR004);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var roots = context.Compilation.SyntaxTrees
            .Select(tree => tree.GetRoot(context.CancellationToken))
            .ToArray();
        var registrations = roots
            .SelectMany(ServiceRegistrationCollector.Collect)
            .ToArray();
        var typedClients = ServiceRegistrationCollector.GetTypedClientTypeNames(registrations);
        if (typedClients.Count == 0)
        {
            return;
        }

        var singletonRegistrations = registrations
            .Where(registration => registration.Kind == ServiceRegistrationKind.Singleton);

        foreach (var singleton in singletonRegistrations)
        {
            if (!ConstructorConsumesTypedClient(roots, singleton.ImplementationTypeName ?? singleton.ServiceTypeName, typedClients))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.HCR004,
                singleton.Location));
        }
    }

    private static bool ConstructorConsumesTypedClient(IEnumerable<SyntaxNode> roots, string singletonTypeName, ISet<string> typedClients)
    {
        var singletonClasses = roots
            .SelectMany(root => root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(type => DeclaredTypeMatchesRegistration(type, singletonTypeName))
            .ToArray();

        foreach (var singletonClass in singletonClasses)
        {
            foreach (var parameter in GetConstructorParameters(singletonClass))
            {
                if (parameter.Type is not null &&
                    TypeNameUtilities.GetComparableNames(UnwrapNullableType(parameter.Type).ToString()).Any(typedClients.Contains))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static TypeSyntax UnwrapNullableType(TypeSyntax type)
    {
        return type is NullableTypeSyntax nullable
            ? nullable.ElementType
            : type;
    }

    private static bool DeclaredTypeMatchesRegistration(ClassDeclarationSyntax classDeclaration, string registrationTypeName)
    {
        registrationTypeName = registrationTypeName.Trim();
        if (registrationTypeName.StartsWith("global::", System.StringComparison.Ordinal))
        {
            registrationTypeName = registrationTypeName.Substring("global::".Length);
        }

        if (registrationTypeName.Contains("."))
        {
            return GetQualifiedClassName(classDeclaration) == registrationTypeName;
        }

        return classDeclaration.Identifier.ValueText == registrationTypeName;
    }

    private static string GetQualifiedClassName(ClassDeclarationSyntax classDeclaration)
    {
        var namespaceName = string.Join(
            ".",
            classDeclaration
                .Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse()
                .Select(ns => ns.Name.ToString()));

        return string.IsNullOrEmpty(namespaceName)
            ? classDeclaration.Identifier.ValueText
            : namespaceName + "." + classDeclaration.Identifier.ValueText;
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
}
