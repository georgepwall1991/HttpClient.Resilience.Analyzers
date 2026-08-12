using System.Collections.Immutable;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.Resilience;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR042_UnsafeMethodHedgingAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR042);

    private int _unsafeCallIndexBuilds;

    /// <summary>
    /// How many times this analyzer instance has built the unsafe-call index. Tests assert
    /// the index stays demand-driven and is never built more than once per compilation.
    /// </summary>
    internal int UnsafeCallIndexBuilds => System.Threading.Volatile.Read(ref _unsafeCallIndexBuilds);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(AnalyzeCompilation);
    }

    private void AnalyzeCompilation(CompilationStartAnalysisContext context)
    {
        var unsafeCallIndex = UnsafeHttpCallIndex.GetOrCreate(context.Compilation);

        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeInvocation(nodeContext, unsafeCallIndex),
            SyntaxKind.InvocationExpression);
    }

    private void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        UnsafeHttpCallIndex unsafeCallIndex)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!ResilienceHandlerInvocation.IsFrameworkHandler(
                invocation,
                context.SemanticModel,
                context.CancellationToken,
                "AddStandardHedgingHandler") ||
            HasUnsafeMethodHedgingGuard(
                invocation,
                context.SemanticModel,
                context.CancellationToken))
        {
            return;
        }

        var typedClient = HttpClientRegistrationChain.TryGetTypedClient(
            invocation,
            context.SemanticModel,
            context.CancellationToken);
        if (typedClient is not null &&
            unsafeCallIndex
                .GetOrBuild(
                    context.CancellationToken,
                    () => System.Threading.Interlocked.Increment(ref _unsafeCallIndexBuilds))
                .TypedClientSendsUnsafeHttpMethod(typedClient))
        {
            ReportDiagnostic(context, invocation);
            return;
        }

        var namedClient = HttpClientRegistrationChain.TryGetNamedClient(
            invocation,
            context.SemanticModel,
            context.CancellationToken);
        if (namedClient is not null &&
            unsafeCallIndex
                .GetOrBuild(
                    context.CancellationToken,
                    () => System.Threading.Interlocked.Increment(ref _unsafeCallIndexBuilds))
                .NamedClientSendsUnsafeHttpMethod(namedClient))
        {
            ReportDiagnostic(context, invocation);
        }
    }

    private static void ReportDiagnostic(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
    {
        var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR042,
            memberAccess.Name.GetLocation()));
    }

    private static bool HasUnsafeMethodHedgingGuard(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return SafeHttpMethodPredicate.ContainsSafeOnlyShouldHandle(
            invocation,
            semanticModel,
            cancellationToken,
            expectedNamespace: "Polly.Hedging",
            requiredOwnerName: "Hedging");
    }
}
