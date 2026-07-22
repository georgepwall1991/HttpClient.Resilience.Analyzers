using HttpClient.Resilience.Analyzers.Analyzers.Lifetime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

public sealed class TestCompilationFactoryTests
{
    [Fact]
    public void Create_ResolvesUnqualifiedHttpClientToSystemNetHttpType()
    {
        const string source = """
            using System.Net.Http;

            public sealed class ApiClient
            {
                private readonly HttpClient _client = new HttpClient();
            }
            """;

        var compilation = TestCompilationFactory.Create("ReferenceIsolationTests", source);

        Assert.DoesNotContain(
            compilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var syntaxTree = Assert.Single(compilation.SyntaxTrees);
        var objectCreation = Assert.Single(syntaxTree.GetRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>());
        var resolvedType = compilation.GetSemanticModel(syntaxTree).GetTypeInfo(objectCreation).Type;

        Assert.Equal("System.Net.Http.HttpClient", resolvedType?.ToDisplayString());
    }

    [Fact]
    public void EnsureNoCompilerErrors_RejectsInvalidTestSource()
    {
        const string source = """
            public sealed class InvalidTestSource
            {
                public int GetValue() => MissingSymbol;
            }
            """;

        var compilation = TestCompilationFactory.Create("InvalidReferenceTests", source);

        var exception = Assert.Throws<InvalidOperationException>(
            () => TestCompilationFactory.EnsureNoCompilerErrors(compilation));

        Assert.Contains("CS0103", exception.Message, StringComparison.Ordinal);
        Assert.Contains("MissingSymbol", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzerVerifier_RejectsInvalidTestSourceByDefault()
    {
        const string source = """
            public sealed class InvalidAnalyzerTestSource
            {
                public int GetValue() => MissingSymbol;
            }
            """;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AnalyzerVerifier<HCR001_NewHttpClientInRequestPathAnalyzer>.GetDiagnosticsAsync(source));

        Assert.Contains("CS0103", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_UsesExecutableOutputForTopLevelStatements()
    {
        const string source = """
            var value = 42;
            System.Console.WriteLine(value);
            """;

        var compilation = TestCompilationFactory.Create("TopLevelStatementTests", source);

        Assert.Equal(OutputKind.ConsoleApplication, compilation.Options.OutputKind);
        TestCompilationFactory.EnsureNoCompilerErrors(compilation);
    }
}
