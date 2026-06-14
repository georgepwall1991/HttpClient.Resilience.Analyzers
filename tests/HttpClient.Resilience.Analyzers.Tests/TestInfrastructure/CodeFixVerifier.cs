using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

internal static class CodeFixVerifier<TAnalyzer, TCodeFix>
    where TAnalyzer : DiagnosticAnalyzer, new()
    where TCodeFix : CodeFixProvider, new()
{
    public static async Task<IReadOnlyList<string>> GetCodeFixTitlesAsync(string source)
    {
        return await WithCodeFixContextAsync(
            source,
            (_, actions) => Task.FromResult<IReadOnlyList<string>>(
                actions.Select(action => action.Title).ToArray()))
            .ConfigureAwait(false);
    }

    public static async Task<string> ApplyFirstCodeFixAsync(string source)
    {
        return await WithCodeFixContextAsync(source, ApplyFirstCodeFixAsync).ConfigureAwait(false);
    }

    private static async Task<string> ApplyFirstCodeFixAsync(
        Document document,
        IReadOnlyList<CodeAction> actions)
    {
        var action = actions.Single();
        var operations = await action.GetOperationsAsync(CancellationToken.None).ConfigureAwait(false);
        var applyChanges = operations.OfType<ApplyChangesOperation>().Single();
        var changedDocument = applyChanges.ChangedSolution.GetDocument(document.Id);

        if (changedDocument is null)
        {
            throw new InvalidOperationException("Code fix did not produce a changed document.");
        }

        var fixedText = await changedDocument.GetTextAsync().ConfigureAwait(false);
        return fixedText.ToString();
    }

    private static async Task<TResult> WithCodeFixContextAsync<TResult>(
        string source,
        Func<Document, IReadOnlyList<CodeAction>, Task<TResult>> action)
    {
        using var workspace = new AdhocWorkspace();

        var project = workspace
            .CurrentSolution
            .AddProject("CodeFixTests", "CodeFixTests", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))
            .AddMetadataReferences(GetReferenceAssemblies());

        var document = project.AddDocument("Test.cs", SourceText.From(source, Encoding.UTF8));
        var compilation = await document.Project.GetCompilationAsync().ConfigureAwait(false);

        if (compilation is null)
        {
            throw new InvalidOperationException("Compilation could not be created.");
        }

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new TAnalyzer()))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false);

        var diagnostic = diagnostics.Single();
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);

        await new TCodeFix().RegisterCodeFixesAsync(context).ConfigureAwait(false);

        return await action(document, actions).ConfigureAwait(false);
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
