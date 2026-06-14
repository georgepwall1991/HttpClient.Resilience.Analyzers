using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
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
        if (!IsDuplicateResilienceHandlerInChain(invocation))
        {
            return;
        }

        var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR040,
            memberAccess.Name.GetLocation()));
    }

    private static bool IsDuplicateResilienceHandlerInChain(InvocationExpressionSyntax invocation)
    {
        return (IsAddStandardResilienceHandlerInvocation(invocation) &&
            CountStandardResilienceHandlersInChain(invocation) > 1) ||
            IsDuplicateNamedResilienceHandlerInChain(invocation);
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

    private static bool IsDuplicateNamedResilienceHandlerInChain(InvocationExpressionSyntax invocation)
    {
        if (!TryGetAddResilienceHandlerName(invocation, out var handlerName))
        {
            return false;
        }

        return GetInvocationChain(invocation)
            .Count(candidate => TryGetAddResilienceHandlerName(candidate, out var candidateName) &&
                candidateName == handlerName) > 1;
    }

    private static IEnumerable<InvocationExpressionSyntax> GetInvocationChain(ExpressionSyntax expression)
    {
        var current = expression;

        while (current is InvocationExpressionSyntax invocation)
        {
            yield return invocation;

            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                yield break;
            }

            current = memberAccess.Expression;
        }
    }

    private static bool TryGetAddResilienceHandlerName(InvocationExpressionSyntax invocation, out string? handlerName)
    {
        handlerName = null;

        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "AddResilienceHandler"
            } ||
            invocation.ArgumentList.Arguments.Count == 0 ||
            invocation.ArgumentList.Arguments[0].Expression is not LiteralExpressionSyntax literal ||
            !literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return false;
        }

        handlerName = literal.Token.ValueText;
        return true;
    }
}
