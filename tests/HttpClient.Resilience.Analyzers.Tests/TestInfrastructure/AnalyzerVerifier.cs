using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

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
        var syntaxTree = CSharpSyntaxTree.ParseText(
            SourceText.From(sources[0], Encoding.UTF8),
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        var syntaxTrees = new List<SyntaxTree> { syntaxTree };

        for (var index = 1; index < sources.Length; index++)
        {
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(
                SourceText.From(sources[index], Encoding.UTF8),
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
                path: $"Test{index}.cs"));
        }

        var compilation = CSharpCompilation.Create(
            "AnalyzerTests",
            syntaxTrees,
            GetReferenceAssemblies(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new TAnalyzer()));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static IReadOnlyList<MetadataReference> GetReferenceAssemblies()
    {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");

        if (trustedPlatformAssemblies is null)
        {
            throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        }

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}
