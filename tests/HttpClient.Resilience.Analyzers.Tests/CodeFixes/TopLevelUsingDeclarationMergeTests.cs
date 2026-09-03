using HttpClient.Resilience.Analyzers.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HttpClient.Resilience.Analyzers.Tests.CodeFixes;

public sealed class TopLevelUsingDeclarationMergeTests
{
    [Fact]
    public async Task TryGetAdjacentDeclaration_MatchesAdjacentPair()
    {
        var assignment = ParseAssignment("""
            HttpResponseMessage response;
            response = await client.GetAsync("https://example.com");
            """);

        var matched = TopLevelUsingDeclarationMerge.TryGetAdjacentDeclaration(
            assignment,
            out var compilationUnit,
            out var declarationStatement,
            out var declaration,
            out var assignmentStatement);

        Assert.True(matched);
        Assert.Same(compilationUnit, assignmentStatement.SyntaxTree.GetRoot());
        Assert.Equal("response", declaration.Declaration.Variables.Single().Identifier.ValueText);
        Assert.Same(declarationStatement.Statement, declaration);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TryGetAdjacentDeclaration_RejectsNonAdjacentPair()
    {
        var assignment = ParseAssignment("""
            HttpResponseMessage response;
            System.Console.WriteLine("Sending request.");
            response = await client.GetAsync("https://example.com");
            """);

        Assert.False(TopLevelUsingDeclarationMerge.TryGetAdjacentDeclaration(
            assignment,
            out _,
            out _,
            out _,
            out _));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TryGetAdjacentDeclaration_RejectsUsingDeclaration()
    {
        var assignment = ParseAssignment("""
            using HttpResponseMessage response = await client.GetAsync("https://example.com");
            response = await client.GetAsync("https://example.com");
            """);

        Assert.False(TopLevelUsingDeclarationMerge.TryGetAdjacentDeclaration(
            assignment,
            out _,
            out _,
            out _,
            out _));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TryGetAdjacentDeclaration_RejectsNameMismatch()
    {
        var assignment = ParseAssignment("""
            HttpResponseMessage response;
            other = await client.GetAsync("https://example.com");
            """);

        Assert.False(TopLevelUsingDeclarationMerge.TryGetAdjacentDeclaration(
            assignment,
            out _,
            out _,
            out _,
            out _));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TryGetAdjacentDeclaration_RejectsDirectiveBetweenStatements()
    {
        var assignment = ParseAssignment("""
            #define DEBUG
            HttpResponseMessage response;
            #if DEBUG
            response = await client.GetAsync("https://example.com");
            #endif
            """);

        Assert.False(TopLevelUsingDeclarationMerge.TryGetAdjacentDeclaration(
            assignment,
            out _,
            out _,
            out _,
            out _));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TryGetAdjacentDeclaration_RejectsBlockScopedAssignment()
    {
        const string source = """
            public sealed class Client
            {
                public void Use()
                {
                    HttpResponseMessage response;
                    response = client.GetAsync("https://example.com").Result;
                }
            }
            """;

        var root = await CSharpSyntaxTree.ParseText(source).GetRootAsync();
        var assignment = root.DescendantNodes().OfType<AssignmentExpressionSyntax>().Single();

        Assert.False(TopLevelUsingDeclarationMerge.TryGetAdjacentDeclaration(
            assignment,
            out _,
            out _,
            out _,
            out _));
    }

    [Fact]
    public void ContainsDirectiveTrivia_DetectsDirectives()
    {
        var withDirective = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText("""
            #define DEBUG
            HttpResponseMessage response;
            #if DEBUG
            response = client.GetAsync("https://example.com");
            #endif
            """).GetRoot();
        var plain = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText("""
            HttpResponseMessage response;
            response = client.GetAsync("https://example.com");
            """).GetRoot();

        Assert.True(TopLevelUsingDeclarationMerge.ContainsDirectiveTrivia(withDirective));
        Assert.False(TopLevelUsingDeclarationMerge.ContainsDirectiveTrivia(plain));
    }

    private static AssignmentExpressionSyntax ParseAssignment(string statements)
    {
        var root = CSharpSyntaxTree.ParseText(statements).GetRoot();
        return root.DescendantNodes().OfType<AssignmentExpressionSyntax>().Single();
    }
}
