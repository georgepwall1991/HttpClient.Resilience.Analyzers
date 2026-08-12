using System.Collections.Immutable;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Tests;

/// <summary>
/// Locks the exact diagnostics every shipped analyzer produces over a fixed corpus.
/// Performance work must leave this snapshot untouched; a diff here means observable
/// behavior changed.
/// </summary>
public sealed class AnalyzerBehaviorSnapshotTests
{
    private const string BaselineFileName = "expected-diagnostics.txt";

    [Fact]
    public async Task CorpusDiagnosticsMatchCommittedBaseline()
    {
        var actual = DiagnosticSnapshot.Render(await GetCorpusDiagnosticsAsync());
        var baselinePath = Path.Combine(CorpusCompilationFactory.CorpusDirectory, BaselineFileName);

        if (DiagnosticSnapshot.ShouldUpdateBaseline())
        {
            UpdateBaseline(actual);
        }

        Assert.True(
            File.Exists(baselinePath),
            $"Baseline '{baselinePath}' is missing. Re-run with {DiagnosticSnapshot.UpdateEnvironmentVariable}=1 to create it.");

        var expected = DiagnosticSnapshot.NormalizeLineEndings(File.ReadAllText(baselinePath));

        Assert.True(
            expected == actual,
            $"""
             Analyzer behavior changed against the committed corpus baseline.
             Re-run with {DiagnosticSnapshot.UpdateEnvironmentVariable}=1 only when the change is intended.

             --- expected ---
             {expected}
             --- actual ---
             {actual}
             """);
    }

    [Fact]
    public async Task CorpusExercisesEveryShippedRule()
    {
        var reported = (await GetCorpusDiagnosticsAsync())
            .Select(diagnostic => diagnostic.Id)
            .ToHashSet(StringComparer.Ordinal);

        var missing = AnalyzerCatalog.DiagnosticIdsInOrder
            .Where(id => !reported.Contains(id))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"The snapshot corpus no longer triggers: {string.Join(", ", missing)}. Add coverage so the baseline keeps protecting every rule.");
    }

    [Fact]
    public void CorpusCompilesWithoutErrors()
    {
        var errors = CorpusCompilationFactory.Create()
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();

        Assert.True(
            errors.Length == 0,
            $"Corpus sources must compile cleanly so analyzers see resolved symbols:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
    }

    private static async Task<ImmutableArray<Diagnostic>> GetCorpusDiagnosticsAsync()
    {
        var compilationWithAnalyzers = CorpusCompilationFactory
            .Create()
            .WithAnalyzers(AnalyzerCatalog.CreateAll());

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static void UpdateBaseline(string actual)
    {
        var repositoryRoot = DiagnosticSnapshot.TryFindRepositoryRoot() ??
            throw new InvalidOperationException("Repository root could not be located to update the baseline.");

        var sourcePath = Path.Combine(
            repositoryRoot,
            "tests",
            "HttpClient.Resilience.Analyzers.Tests",
            "Corpus",
            BaselineFileName);

        File.WriteAllText(sourcePath, actual.Replace("\n", "\r\n"));
        File.WriteAllText(
            Path.Combine(CorpusCompilationFactory.CorpusDirectory, BaselineFileName),
            actual);
    }
}
