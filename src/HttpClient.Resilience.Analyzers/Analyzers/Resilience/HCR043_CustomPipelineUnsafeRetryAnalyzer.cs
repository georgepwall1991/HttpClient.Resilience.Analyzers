using System.Collections.Immutable;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.Resilience;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR043_CustomPipelineUnsafeRetryAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR043);

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
                "AddResilienceHandler"))
        {
            return;
        }

        var addRetries = ResilienceRetryInvocation.FindBuilderBoundAddRetries(
            invocation,
            context.SemanticModel,
            context.CancellationToken);
        if (addRetries.IsDefaultOrEmpty)
        {
            return;
        }

        var typedClient = HttpClientRegistrationChain.TryGetTypedClient(
            invocation,
            context.SemanticModel,
            context.CancellationToken);
        var namedClient = HttpClientRegistrationChain.TryGetNamedClient(
            invocation,
            context.SemanticModel,
            context.CancellationToken);
        if (typedClient is null && namedClient is null)
        {
            return;
        }

        var snapshot = unsafeCallIndex.GetOrBuild(
            context.CancellationToken,
            () => System.Threading.Interlocked.Increment(ref _unsafeCallIndexBuilds));

        var sendsUnsafe =
            (typedClient is not null && snapshot.TypedClientSendsUnsafeHttpMethod(typedClient)) ||
            (namedClient is not null && snapshot.NamedClientSendsUnsafeHttpMethod(namedClient));
        if (!sendsUnsafe)
        {
            return;
        }

        foreach (var addRetry in addRetries)
        {
            if (RetryUnsafeMethodGuard.HasVisibleGuard(
                    addRetry,
                    context.SemanticModel,
                    context.CancellationToken))
            {
                continue;
            }

            ReportDiagnostic(context, addRetry);
        }
    }

    private static void ReportDiagnostic(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax addRetry)
    {
        if (addRetry.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        var location = memberAccess.Name is GenericNameSyntax genericName
            ? genericName.Identifier.GetLocation()
            : memberAccess.Name.GetLocation();
        context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.HCR043, location));
    }
}
