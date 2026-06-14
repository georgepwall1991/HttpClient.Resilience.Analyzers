using System.Collections.Immutable;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.Concurrency;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR080_UnboundedHttpFanOutAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] HttpCallMethodNames =
    {
        "DeleteAsync",
        "GetAsync",
        "PatchAsync",
        "PostAsync",
        "PutAsync",
        "SendAsync"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR080);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!IsTaskWhenAll(invocation) || invocation.ArgumentList.Arguments.Count == 0)
        {
            return;
        }

        if (!ContainsSelectWithHttpCall(invocation.ArgumentList.Arguments[0].Expression))
        {
            return;
        }

        var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR080,
            memberAccess.Name.GetLocation()));
    }

    private static bool IsTaskWhenAll(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.ValueText: "Task" },
            Name.Identifier.ValueText: "WhenAll"
        };
    }

    private static bool ContainsSelectWithHttpCall(ExpressionSyntax expression)
    {
        return expression
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(IsSelectInvocationWithHttpCall);
    }

    private static bool IsSelectInvocationWithHttpCall(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Select"
            })
        {
            return false;
        }

        return invocation.ArgumentList.Arguments
            .Select(argument => argument.Expression)
            .OfType<LambdaExpressionSyntax>()
            .Any(lambda => !UsesSemaphoreGate(lambda) &&
                lambda.Body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(IsHttpCall));
    }

    private static bool IsHttpCall(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            HttpCallMethodNames.Contains(memberAccess.Name.Identifier.ValueText, System.StringComparer.Ordinal);
    }

    private static bool UsesSemaphoreGate(LambdaExpressionSyntax lambda)
    {
        var invocationNames = lambda.Body
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => invocation.Expression)
            .OfType<MemberAccessExpressionSyntax>()
            .Select(memberAccess => memberAccess.Name.Identifier.ValueText)
            .ToArray();

        return invocationNames.Contains("WaitAsync", System.StringComparer.Ordinal) &&
            invocationNames.Contains("Release", System.StringComparer.Ordinal);
    }
}
