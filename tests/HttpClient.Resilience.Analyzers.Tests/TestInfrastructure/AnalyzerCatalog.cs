using System.Collections.Immutable;
using System.Reflection;
using HttpClient.Resilience.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

/// <summary>
/// Discovers every shipped analyzer by reflection so suites that must cover the
/// whole rule set cannot silently miss a newly added rule.
/// </summary>
internal static class AnalyzerCatalog
{
    public static Assembly AnalyzerAssembly { get; } = typeof(DiagnosticIds).Assembly;

    public static ImmutableArray<Type> AnalyzerTypes { get; } = AnalyzerAssembly
        .GetTypes()
        .Where(type => type is { IsAbstract: false, IsPublic: true } &&
            typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
        .OrderBy(type => type.FullName, StringComparer.Ordinal)
        .ToImmutableArray();

    public static ImmutableArray<string> DiagnosticIdsInOrder { get; } = typeof(DiagnosticIds)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .OrderBy(id => id, StringComparer.Ordinal)
        .ToImmutableArray();

    public static ImmutableArray<DiagnosticAnalyzer> CreateAll()
    {
        return AnalyzerTypes
            .Select(Create)
            .ToImmutableArray();
    }

    public static DiagnosticAnalyzer Create(Type analyzerType)
    {
        return (DiagnosticAnalyzer)Activator.CreateInstance(analyzerType)!;
    }

    /// <summary>
    /// xUnit member data over every analyzer type, keyed by name so failures name the rule.
    /// </summary>
    public static TheoryData<string> AnalyzerTypeNames()
    {
        var data = new TheoryData<string>();
        foreach (var analyzerType in AnalyzerTypes)
        {
            data.Add(analyzerType.FullName!);
        }

        return data;
    }

    public static DiagnosticAnalyzer CreateByFullName(string fullName)
    {
        var analyzerType = AnalyzerTypes.Single(type => type.FullName == fullName);
        return Create(analyzerType);
    }
}
