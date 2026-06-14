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
        var comparableSingletonTypeName = TypeNameUtilities.ToSimpleName(singletonTypeName);
        var singletonClass = roots
            .SelectMany(root => root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .FirstOrDefault(type => type.Identifier.ValueText == comparableSingletonTypeName);

        if (singletonClass is null)
        {
            return false;
        }

        foreach (var parameter in GetConstructorParameters(singletonClass))
        {
            if (parameter.Type is not null &&
                TypeNameUtilities.GetComparableNames(parameter.Type.ToString()).Any(typedClients.Contains))
            {
                return true;
            }
        }

        return false;
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
