using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

internal static class TestCompilationFactory
{
    private const string RepositoryAssemblyPrefix = "HttpClient.Resilience.Analyzers";

    public static ImmutableArray<MetadataReference> References { get; } = CreateReferences();

    public static CSharpCompilation Create(string assemblyName, params string[] sources)
    {
        if (sources.Length == 0)
        {
            throw new ArgumentException("At least one source file is required.", nameof(sources));
        }

        var syntaxTrees = sources
            .Select((source, index) => CSharpSyntaxTree.ParseText(
                SourceText.From(source, Encoding.UTF8),
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
                path: index == 0 ? "Test.cs" : $"Test{index}.cs"))
            .ToArray();

        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");

        if (trustedPlatformAssemblies is null)
        {
            throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        }

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Where(path => !Path.GetFileNameWithoutExtension(path).StartsWith(
                RepositoryAssemblyPrefix,
                StringComparison.OrdinalIgnoreCase))
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray<MetadataReference>();
    }
}
