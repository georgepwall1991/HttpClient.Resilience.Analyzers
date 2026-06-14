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
        var typedClientRegistrations = registrations
            .Where(registration => registration.Kind == ServiceRegistrationKind.HttpClient)
            .ToArray();
        var typedClients = ServiceRegistrationCollector.GetTypedClientTypeNames(typedClientRegistrations);
        if (typedClients.Count == 0)
        {
            return;
        }

        foreach (var registration in registrations.Where(IsStandaloneServiceRegistration))
        {
            if (!MatchesAnyTypedClientRegistration(registration, typedClientRegistrations))
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

    private static bool MatchesAnyTypedClientRegistration(
        ServiceRegistrationModel registration,
        IEnumerable<ServiceRegistrationModel> typedClientRegistrations)
    {
        return GetRegisteredTypeNames(registration)
            .Any(registrationTypeName => typedClientRegistrations
                .SelectMany(GetRegisteredTypeNames)
                .Any(typedClientTypeName => TypeNamesMatch(registrationTypeName, typedClientTypeName)));
    }

    private static IEnumerable<string> GetRegisteredTypeNames(ServiceRegistrationModel registration)
    {
        yield return registration.ServiceTypeName;

        if (registration.ImplementationTypeName is not null)
        {
            yield return registration.ImplementationTypeName;
        }
    }

    private static bool TypeNamesMatch(string left, string right)
    {
        left = NormalizeTypeName(left);
        right = NormalizeTypeName(right);

        if (IsQualifiedTypeName(left) && IsQualifiedTypeName(right))
        {
            return left == right;
        }

        return TypeNameUtilities.ToSimpleName(left) == TypeNameUtilities.ToSimpleName(right);
    }

    private static string NormalizeTypeName(string typeName)
    {
        typeName = typeName.Trim();
        return typeName.StartsWith("global::", System.StringComparison.Ordinal)
            ? typeName.Substring("global::".Length)
            : typeName;
    }

    private static bool IsQualifiedTypeName(string typeName)
    {
        return typeName.Contains(".");
    }
}
