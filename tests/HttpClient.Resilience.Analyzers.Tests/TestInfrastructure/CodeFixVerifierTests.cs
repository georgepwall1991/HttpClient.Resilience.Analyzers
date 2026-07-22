using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

#pragma warning disable RS1001, RS2008 // Test-only analyzer is instantiated directly and has no product release entry.

public sealed class CodeFixVerifierTests
{
    [Fact]
    public async Task ApplyFirstCodeFixAsync_RejectsCompilerErrorsInFixedSource()
    {
        const string source = "public sealed class ValidTestSource { }";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CodeFixVerifier<TestAnalyzer, InvalidCodeFixProvider>.ApplyFirstCodeFixAsync(source));

        Assert.Contains("CS0103", exception.Message, StringComparison.Ordinal);
        Assert.Contains("MissingSymbol", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyFirstCodeFixAsync_RejectsFixedSourceThatRetainsDiagnostic()
    {
        const string source = "public sealed class ValidTestSource { }";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CodeFixVerifier<TestAnalyzer, IneffectiveCodeFixProvider>.ApplyFirstCodeFixAsync(source));

        Assert.Contains("still reports diagnostic TEST001", exception.Message, StringComparison.Ordinal);
    }

    private sealed class TestAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor Descriptor = new(
            "TEST001",
            "Test diagnostic",
            "Test diagnostic",
            "Testing",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(Descriptor);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(
                syntaxContext => syntaxContext.ReportDiagnostic(
                    Diagnostic.Create(Descriptor, syntaxContext.Node.GetLocation())),
                SyntaxKind.ClassDeclaration);
        }
    }

    private sealed class InvalidCodeFixProvider : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            ImmutableArray.Create("TEST001");

        public override FixAllProvider? GetFixAllProvider()
        {
            return null;
        }

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Introduce compiler error",
                    _ => Task.FromResult(context.Document.WithText(SourceText.From(
                        "public sealed class InvalidFixedSource { public int Value => MissingSymbol; }"))),
                    equivalenceKey: "IntroduceCompilerError"),
                context.Diagnostics.Single());

            return Task.CompletedTask;
        }
    }

    private sealed class IneffectiveCodeFixProvider : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            ImmutableArray.Create("TEST001");

        public override FixAllProvider? GetFixAllProvider()
        {
            return null;
        }

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Leave diagnostic behind",
                    _ => Task.FromResult(context.Document.WithText(SourceText.From(
                        "// Attempted fix\npublic sealed class ValidTestSource { }"))),
                    equivalenceKey: "LeaveDiagnosticBehind"),
                context.Diagnostics.Single());

            return Task.CompletedTask;
        }
    }
}

#pragma warning restore RS1001, RS2008
