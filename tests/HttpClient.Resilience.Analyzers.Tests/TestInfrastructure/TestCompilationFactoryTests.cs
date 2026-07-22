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
}
