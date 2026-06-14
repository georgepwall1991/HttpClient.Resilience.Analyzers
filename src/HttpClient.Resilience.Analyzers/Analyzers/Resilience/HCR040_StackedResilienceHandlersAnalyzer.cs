using System.Collections.Immutable;
using HttpClient.Resilience.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.Resilience;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR040_StackedResilienceHandlersAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR040);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!IsAddStandardResilienceHandlerInvocation(invocation))
        {
            return;
        }

        if (CountStandardResilienceHandlersInChain(invocation) <= 1)
        {
            return;
        }

        var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR040,
            memberAccess.Name.GetLocation()));
    }

    private static int CountStandardResilienceHandlersInChain(ExpressionSyntax expression)
    {
        var count = 0;
        var current = expression;

        while (current is InvocationExpressionSyntax invocation)
        {
            if (IsAddStandardResilienceHandlerInvocation(invocation))
            {
                count++;
            }

            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                current = memberAccess.Expression;
                continue;
            }

            break;
        }

        return count;
    }

    private static bool IsAddStandardResilienceHandlerInvocation(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is MemberAccessExpressionSyntax
        {
            Name.Identifier.ValueText: "AddStandardResilienceHandler"
        };
    }
}
