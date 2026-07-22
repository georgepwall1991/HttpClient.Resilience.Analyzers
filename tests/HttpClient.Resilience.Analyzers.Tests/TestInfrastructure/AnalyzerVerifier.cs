using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

internal static class AnalyzerVerifier<TAnalyzer>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        return await GetDiagnosticsAsync(new[] { source });
    }

    public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(params string[] sources)
    {
        return await GetDiagnosticsCoreAsync(validateCompilerErrors: true, sources);
    }

    public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAllowingCompilerErrorsAsync(string source)
    {
        return await GetDiagnosticsCoreAsync(validateCompilerErrors: false, new[] { source });
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsCoreAsync(
        bool validateCompilerErrors,
        string[] sources)
    {
        var compilation = TestCompilationFactory.Create("AnalyzerTests", sources);
        if (validateCompilerErrors)
        {
            TestCompilationFactory.EnsureNoCompilerErrors(compilation);
        }

        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new TAnalyzer()));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
