using System.Linq;
using System.Threading.Tasks;
using HttpClient.Resilience.Analyzers.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace HttpClient.Resilience.Analyzers.Tests.CodeFixes;

public sealed class UsingDeclarationEscapeGateTests
{
    [Fact]
    public async Task EmptyName_NeverEscapes()
    {
        var declaration = ParseResponseDeclaration("return Task.FromResult(0);");

        Assert.False(UsingDeclarationEscapeGate.VariableEscapesScope(declaration, string.Empty));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task DetachedNode_NeverEscapes()
    {
        var detached = SyntaxFactory.IdentifierName("response");

        Assert.False(UsingDeclarationEscapeGate.VariableEscapesScope(detached, "response"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ReturnedIdentifier_Escapes()
    {
        var declaration = ParseResponseDeclaration("return response;");

        Assert.True(UsingDeclarationEscapeGate.VariableEscapesScope(declaration, "response"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ReturnedTuple_Escapes()
    {
        var declaration = ParseResponseDeclaration("return (response, 1);");

        Assert.True(UsingDeclarationEscapeGate.VariableEscapesScope(declaration, "response"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task StoredIntoMember_Escapes()
    {
        var declaration = ParseResponseDeclaration("this.pending = response;");

        Assert.True(UsingDeclarationEscapeGate.VariableEscapesScope(declaration, "response"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task PassedAsArgument_Escapes()
    {
        var declaration = ParseResponseDeclaration("Takes(response);");

        Assert.True(UsingDeclarationEscapeGate.VariableEscapesScope(declaration, "response"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ParenthesizedArgument_Escapes()
    {
        var declaration = ParseResponseDeclaration("Takes((response));");

        Assert.True(UsingDeclarationEscapeGate.VariableEscapesScope(declaration, "response"));
        await Task.CompletedTask;
    }
    [Fact]
    public async Task ObjectInitializerMember_Escapes()
    {
        var declaration = ParseResponseDeclaration("var holder = new Holder { Value = response };");

        Assert.True(UsingDeclarationEscapeGate.VariableEscapesScope(declaration, "response"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task CollectionInitializerElement_Escapes()
    {
        var declaration = ParseResponseDeclaration("var items = new System.Collections.Generic.List<object> { response };");

        Assert.True(UsingDeclarationEscapeGate.VariableEscapesScope(declaration, "response"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task MemberCall_DoesNotEscape()
    {
        var declaration = ParseResponseDeclaration("_ = response.Content.ReadAsStringAsync();");

        Assert.False(UsingDeclarationEscapeGate.VariableEscapesScope(declaration, "response"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task MemberAccessAsArgument_DoesNotEscape()
    {
        var declaration = ParseResponseDeclaration("Takes(response.Content);");

        Assert.False(UsingDeclarationEscapeGate.VariableEscapesScope(declaration, "response"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task LocalReassignment_DoesNotEscape()
    {
        var declaration = ParseResponseDeclaration("response = other;");

        Assert.False(UsingDeclarationEscapeGate.VariableEscapesScope(declaration, "response"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TypeTest_DoesNotEscape()
    {
        var declaration = ParseResponseDeclaration("if (response is not null) { Takes(1); }");

        Assert.False(UsingDeclarationEscapeGate.VariableEscapesScope(declaration, "response"));
        await Task.CompletedTask;
    }

    private static LocalDeclarationStatementSyntax ParseResponseDeclaration(string tailStatement)
    {
        var root = CSharpSyntaxTree.ParseText(
            """
            using System.Net.Http;
            using System.Threading.Tasks;

            public sealed class Client
            {
                private HttpResponseMessage? pending;

                public async Task UseAsync(HttpClient client)
                {
                    var response = await client.GetAsync("https://example.com");
            """
            + "\n                    " + tailStatement + "\n                }\n            }\n").GetCompilationUnitRoot();

        return root.DescendantNodes()
            .OfType<LocalDeclarationStatementSyntax>()
            .First(declaration => declaration.Declaration.Variables.Any(variable => variable.Identifier.ValueText == "response"));
    }
}
