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
public sealed class HCR005_DuplicateTypedClientRegistrationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR005);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var registrations = GetCompilationRegistrations(context);
        var typedClients = ServiceRegistrationCollector.GetTypedClientTypeNames(registrations);
        if (typedClients.Count == 0)
        {
            return;
        }

        foreach (var registration in registrations.Where(IsStandaloneServiceRegistration))
        {
            if (!registration.MatchesAnyType(typedClients))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.HCR005,
                registration.Location));
        }
    }

    private static IReadOnlyList<ServiceRegistrationModel> GetCompilationRegistrations(CompilationAnalysisContext context)
    {
        return context.Compilation.SyntaxTrees
            .Select(tree => tree.GetRoot(context.CancellationToken))
            .SelectMany(ServiceRegistrationCollector.Collect)
            .ToArray();
    }

    private static bool IsStandaloneServiceRegistration(ServiceRegistrationModel registration)
    {
        return registration.Kind is
            ServiceRegistrationKind.Singleton or
            ServiceRegistrationKind.Scoped or
            ServiceRegistrationKind.Transient;
    }
}
