using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

/// <summary>
/// Builds the fixed multi-file compilation used by the behavior snapshot and the
/// analyzer work guardrails. Corpus sources ship as <c>.cs.txt</c> so they are copied
/// next to the test assembly instead of being compiled into it.
/// </summary>
internal static class CorpusCompilationFactory
{
    private const string CorpusDirectoryName = "Corpus";
    private const string CorpusSourceExtension = ".cs.txt";

    public static ImmutableArray<CorpusSource> Sources { get; } = LoadSources();

    public static string CorpusDirectory { get; } =
        Path.Combine(AppContext.BaseDirectory, CorpusDirectoryName);

    public static CSharpCompilation Create(string assemblyName = "CorpusCompilation")
    {
        return Create(assemblyName, Sources);
    }

    /// <summary>
    /// Creates a compilation whose corpus content is repeated <paramref name="copies"/> times.
    /// Each copy gets its own file names and type-name suffix, which lets a test observe how
    /// analyzer work scales with compilation size.
    /// </summary>
    public static CSharpCompilation CreateScaled(int copies, string assemblyName = "ScaledCorpusCompilation")
    {
        if (copies < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(copies), copies, "At least one copy is required.");
        }

        var scaled = ImmutableArray.CreateBuilder<CorpusSource>();
        for (var copy = 0; copy < copies; copy++)
        {
            foreach (var source in Sources)
            {
                scaled.Add(copy == 0
                    ? source
                    : new CorpusSource(
                        $"Copy{copy}.{source.FileName}",
                        source.Text.Replace("Corpus", $"Corpus{copy}")));
            }
        }

        return Create(assemblyName, scaled.ToImmutable());
    }

    private static CSharpCompilation Create(string assemblyName, ImmutableArray<CorpusSource> sources)
    {
        var syntaxTrees = sources
            .Select(source => CSharpSyntaxTree.ParseText(
                SourceText.From(source.Text, Encoding.UTF8),
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
                path: source.FileName))
            .ToArray();

        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            TestCompilationFactory.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static ImmutableArray<CorpusSource> LoadSources()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, CorpusDirectoryName);
        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException(
                $"Corpus directory '{directory}' is missing. Confirm the corpus files are copied to the output directory.");
        }

        var sources = Directory
            .EnumerateFiles(directory, "*" + CorpusSourceExtension, SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .Select(path => new CorpusSource(
                Path.GetFileName(path)[..^".txt".Length],
                File.ReadAllText(path)))
            .ToImmutableArray();

        if (sources.Length == 0)
        {
            throw new InvalidOperationException($"No corpus sources were found in '{directory}'.");
        }

        return sources;
    }

    public readonly record struct CorpusSource(string FileName, string Text);
}
