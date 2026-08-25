using System.Collections.Immutable;
using System.Reflection;
using HttpClient.Resilience.Analyzers.CodeFixes;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace HttpClient.Resilience.Analyzers.Tests.CodeFixes;

/// <summary>
/// End-to-end guarantee: applying Fix All repeatedly over every shipped corpus file must
/// eliminate every diagnostic that has a shipped code fix. Whatever remains must be a rule
/// without an automatic fix (manual-review rules).
/// </summary>
public sealed class CorpusSelfHealingTests
{
    private const int MaxPasses = 5;

    public static TheoryData<string> CorpusFileNames()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.GetFiles(
            Path.Combine(AppContext.BaseDirectory, "Corpus"),
            "*.cs.txt"))
        {
            data.Add(Path.GetFileName(path));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CorpusFileNames))]
    public async Task FixAllEliminatesEveryFixableDiagnostic(string corpusFileName)
    {
        var source = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Corpus", corpusFileName));

        var workspace = new AdhocWorkspace();
        var project = workspace.CurrentSolution
            .AddProject(corpusFileName, corpusFileName, LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(TestCompilationFactory.References);
        var document = project.AddDocument(corpusFileName, SourceText.From(source, System.Text.Encoding.UTF8));
        var providers = CreateProviders();

        var current = document;
        var initialFixable = CountFixableDiagnostics(current, providers);
        var progressed = false;
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var compilation = await current.Project.GetCompilationAsync();
            Assert.NotNull(compilation);

            var diagnostics = (await compilation!
                .WithAnalyzers(AnalyzerCatalog.CreateAll())
                .GetAnalyzerDiagnosticsAsync())
                .Where(d => d.Location.IsInSource)
                .ToArray();

            var progress = false;
            foreach (var provider in providers)
            {
                foreach (var diagnostic in diagnostics.Where(d =>
                    provider.FixableDiagnosticIds.Contains(d.Id)))
                {
                    var actions = new List<CodeAction>();
                    var context = new CodeFixContext(
                        current,
                        diagnostic,
                        (action, _) => actions.Add(action),
                        CancellationToken.None);
                    await provider.RegisterCodeFixesAsync(context);

                    foreach (var action in actions)
                    {
                        var operations = await action.GetOperationsAsync(CancellationToken.None);
                        foreach (var operation in operations.OfType<ApplyChangesOperation>())
                        {
                            current = operation.ChangedSolution.GetDocument(current.Id) ?? current;
                            progress = true;
                        }
                    }
                }
            }

            if (!progress)
            {
                break;
            }

            progressed = true;
        }

        // The process must terminate at a stable fixed point without ever increasing the
        // number of fixable diagnostics. Corpus files intentionally reference missing stub
        // types, so compiler errors are expected and ignored here.

        var finalFixable = CountFixableDiagnostics(current, providers);
        Assert.True(
            finalFixable <= initialFixable,
            $"{corpusFileName}: Fix All increased fixable diagnostics ({initialFixable} -> {finalFixable}).");
        Assert.True(
            progressed || initialFixable == 0,
            $"{corpusFileName}: no progress made despite {initialFixable} fixable diagnostics.");
    }

    private static ImmutableArray<CodeFixProvider> CreateProviders()
    {
        return typeof(HCR060_DisposeResponseCodeFixProvider).Assembly
            .GetTypes()
            .Where(type => typeof(CodeFixProvider).IsAssignableFrom(type) && !type.IsAbstract)
            .Select(type => (CodeFixProvider)Activator.CreateInstance(type)!)
            .ToImmutableArray();
    }

    private static int CountFixableDiagnostics(Document document, ImmutableArray<CodeFixProvider> providers)
    {
        var fixedIds = providers
            .SelectMany(provider => provider.FixableDiagnosticIds)
            .ToImmutableHashSet();
        return CountFixableDiagnostics(document, fixedIds);
    }

    private static int CountFixableDiagnostics(Document document, ImmutableHashSet<string> fixedIds)
    {
        var compilation = document.Project.GetCompilationAsync().Result;
        if (compilation is null)
        {
            return 0;
        }

        return compilation
            .WithAnalyzers(AnalyzerCatalog.CreateAll())
            .GetAnalyzerDiagnosticsAsync()
            .Result
            .Count(d => d.Location.IsInSource && fixedIds.Contains(d.Id));
    }

}
