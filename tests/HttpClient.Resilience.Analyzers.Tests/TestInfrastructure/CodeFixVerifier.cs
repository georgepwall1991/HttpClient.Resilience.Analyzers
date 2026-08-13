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
            (_, _, actions) => Task.FromResult<IReadOnlyList<string>>(
                actions.Select(action => action.Title).ToArray()))
            .ConfigureAwait(false);
    }

    public static async Task<string> ApplyFirstCodeFixAsync(string source)
    {
        return await WithCodeFixContextAsync(source, ApplyFirstCodeFixAsync).ConfigureAwait(false);
    }

    private static async Task<string> ApplyFirstCodeFixAsync(
        Document document,
        Diagnostic diagnostic,
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

        var changedCompilation = await changedDocument.Project.GetCompilationAsync().ConfigureAwait(false);
        if (changedCompilation is null)
        {
            throw new InvalidOperationException("Code fix output compilation could not be created.");
        }

        TestCompilationFactory.EnsureNoCompilerErrors(changedCompilation);

        var remainingDiagnostics = await changedCompilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new TAnalyzer()))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false);

        if (remainingDiagnostics.Any(remaining => remaining.Id == diagnostic.Id))
        {
            throw new InvalidOperationException(
                $"Code fix output still reports diagnostic {diagnostic.Id}.");
        }

        var fixedText = await changedDocument.GetTextAsync().ConfigureAwait(false);
        return fixedText.ToString();
    }

    private static async Task<TResult> WithCodeFixContextAsync<TResult>(
        string source,
        Func<Document, Diagnostic, IReadOnlyList<CodeAction>, Task<TResult>> action)
    {
        using var workspace = new AdhocWorkspace();

        var project = workspace
            .CurrentSolution
            .AddProject("CodeFixTests", "CodeFixTests", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))
            .AddMetadataReferences(TestCompilationFactory.References);

        var document = project.AddDocument("Test.cs", SourceText.From(source, Encoding.UTF8));
        var compilation = await document.Project.GetCompilationAsync().ConfigureAwait(false);

        if (compilation is null)
        {
            throw new InvalidOperationException("Compilation could not be created.");
        }

        TestCompilationFactory.EnsureNoCompilerErrors(compilation);

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new TAnalyzer()))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false);

        if (diagnostics.Length == 0)
        {
            throw new InvalidOperationException("Analyzer did not report a diagnostic to fix.");
        }

        var diagnostic = diagnostics
            .OrderBy(candidate => candidate.Location.SourceSpan.Start)
            .First();
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);

        await new TCodeFix().RegisterCodeFixesAsync(context).ConfigureAwait(false);

        return await action(document, diagnostic, actions).ConfigureAwait(false);
    }

    public static async Task<string> ApplyFirstCodeFixAllowingRemainingAsync(string source)
    {
        return await WithCodeFixContextAsync(source, ApplyFirstCodeFixAllowingRemainingAsync).ConfigureAwait(false);
    }

    private static async Task<string> ApplyFirstCodeFixAllowingRemainingAsync(
        Document document,
        Diagnostic diagnostic,
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

        var changedCompilation = await changedDocument.Project.GetCompilationAsync().ConfigureAwait(false);
        if (changedCompilation is null)
        {
            throw new InvalidOperationException("Code fix output compilation could not be created.");
        }

        TestCompilationFactory.EnsureNoCompilerErrors(changedCompilation);

        var fixedText = await changedDocument.GetTextAsync().ConfigureAwait(false);
        return fixedText.ToString();
    }

    public static async Task<string> ApplyFixAllInDocumentAsync(string source)
    {
        using var workspace = new AdhocWorkspace();

        var project = workspace
            .CurrentSolution
            .AddProject("CodeFixTests", "CodeFixTests", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .WithParseOptions(CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))
            .AddMetadataReferences(TestCompilationFactory.References);

        var document = project.AddDocument("Test.cs", SourceText.From(source, Encoding.UTF8));
        var compilation = await document.Project.GetCompilationAsync().ConfigureAwait(false);
        if (compilation is null)
        {
            throw new InvalidOperationException("Compilation could not be created.");
        }

        TestCompilationFactory.EnsureNoCompilerErrors(compilation);

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new TAnalyzer()))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false);

        if (diagnostics.Length == 0)
        {
            throw new InvalidOperationException("Analyzer did not report a diagnostic to fix.");
        }

        var provider = new TCodeFix();
        var fixAllProvider = provider.GetFixAllProvider();
        if (fixAllProvider is null)
        {
            throw new InvalidOperationException("Code fix does not support Fix All.");
        }

        var firstDiagnostic = diagnostics
            .OrderBy(candidate => candidate.Location.SourceSpan.Start)
            .First();
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            firstDiagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);
        await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
        var equivalenceKey = actions.Single().EquivalenceKey;

        var fixAllContext = new FixAllContext(
            document,
            provider,
            FixAllScope.Document,
            equivalenceKey,
            provider.FixableDiagnosticIds,
            new DocumentDiagnosticProvider(diagnostics),
            CancellationToken.None);

        var fixAllAction = await fixAllProvider.GetFixAsync(fixAllContext).ConfigureAwait(false);
        if (fixAllAction is null)
        {
            throw new InvalidOperationException("Fix All did not produce a code action.");
        }

        var operations = await fixAllAction.GetOperationsAsync(CancellationToken.None).ConfigureAwait(false);
        var applyChanges = operations.OfType<ApplyChangesOperation>().Single();
        var changedDocument = applyChanges.ChangedSolution.GetDocument(document.Id);
        if (changedDocument is null)
        {
            throw new InvalidOperationException("Fix All did not produce a changed document.");
        }

        var changedCompilation = await changedDocument.Project.GetCompilationAsync().ConfigureAwait(false);
        if (changedCompilation is null)
        {
            throw new InvalidOperationException("Fix All output compilation could not be created.");
        }

        TestCompilationFactory.EnsureNoCompilerErrors(changedCompilation);

        var remainingDiagnostics = await changedCompilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new TAnalyzer()))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false);

        if (remainingDiagnostics.Any(remaining => provider.FixableDiagnosticIds.Contains(remaining.Id)))
        {
            throw new InvalidOperationException(
                $"Fix All output still reports diagnostic {string.Join(", ", remainingDiagnostics.Select(remaining => remaining.Id))}.");
        }

        var fixedText = await changedDocument.GetTextAsync().ConfigureAwait(false);
        return fixedText.ToString();
    }

    private sealed class DocumentDiagnosticProvider : FixAllContext.DiagnosticProvider
    {
        private readonly ImmutableArray<Diagnostic> _diagnostics;

        public DocumentDiagnosticProvider(ImmutableArray<Diagnostic> diagnostics)
        {
            _diagnostics = diagnostics;
        }

        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(
            Document document,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<Diagnostic>>(_diagnostics);
        }

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(
            Project project,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<Diagnostic>>(_diagnostics);
        }

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(
            Project project,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<Diagnostic>>(Array.Empty<Diagnostic>());
        }
    }
}
