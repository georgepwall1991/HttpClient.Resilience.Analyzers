using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.ResponseLifetime;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR065_RequestMessageResendAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR065);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeBlock, SyntaxKind.Block);
    }

    private static void AnalyzeBlock(SyntaxNodeAnalysisContext context)
    {
        var block = (BlockSyntax)context.Node;
        var sends = block.Statements
            .SelectMany(statement => statement.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            .Where(invocation => IsSendInvocation(invocation, context.SemanticModel, context.CancellationToken))
            .ToArray();

        if (sends.Length == 0)
        {
            return;
        }

        var sentSymbols = new Dictionary<ISymbol, InvocationExpressionSyntax>(SymbolEqualityComparer.Default);
        foreach (var send in sends)
        {
            var requestArgument = send.ArgumentList.Arguments.FirstOrDefault();
            if (requestArgument is null)
            {
                continue;
            }

            var requestSymbol = context.SemanticModel.GetSymbolInfo(
                requestArgument.Expression,
                context.CancellationToken).Symbol;
            if (requestSymbol is not ILocalSymbol and not IParameterSymbol)
            {
                continue;
            }

            // A send inside a loop resends the same request on the second iteration
            // unless the loop body assigns a fresh request first.
            var loop = send.FirstAncestorOrSelf<StatementSyntax>(
                ancestor => ancestor is ForStatementSyntax or ForEachStatementSyntax or
                    ForEachVariableStatementSyntax or WhileStatementSyntax or DoStatementSyntax);
            if (loop is not null &&
                ReferenceEquals(loop.Parent, block) &&
                !SyntaxTransparency.LocalIsReassignedBetween(
                    loop,
                    requestSymbol.Name,
                    loop.SpanStart,
                    loop.Span.End))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.HCR065,
                    requestArgument.GetLocation()));
                continue;
            }

            if (!sentSymbols.TryGetValue(requestSymbol, out var firstSend))
            {
                sentSymbols[requestSymbol] = send;
                continue;
            }

            if (SyntaxTransparency.LocalIsReassignedBetween(
                    block,
                    requestSymbol.Name,
                    firstSend.SpanStart,
                    send.SpanStart))
            {
                sentSymbols[requestSymbol] = send;
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.HCR065,
                requestArgument.GetLocation()));
        }
    }

    private static bool IsSendInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name.Identifier.ValueText is not ("Send" or "SendAsync"))
        {
            return false;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return MethodSendsHttpRequest(method);
        }

        var candidateMethods = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
        if (candidateMethods.Length > 0)
        {
            return candidateMethods.All(MethodSendsHttpRequest);
        }

        // Unresolved fallback: base.SendAsync inside a handler SendAsync override, or a
        // receiver that visibly declares an HttpClient type.
        if (memberAccess.Expression is BaseExpressionSyntax)
        {
            return invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>() is { } method1 &&
                method1.Identifier.ValueText == "SendAsync" &&
                method1.Modifiers.Any(SyntaxKind.OverrideKeyword);
        }

        return ReceiverLooksLikeHttpClient(memberAccess.Expression);
    }

    private static bool MethodSendsHttpRequest(IMethodSymbol method)
    {
        var original = method.ReducedFrom ?? method;
        return original.Name is "Send" or "SendAsync" &&
            original.Parameters.Length > 0 &&
            original.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                "global::System.Net.Http.HttpRequestMessage" &&
            (original.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                "global::System.Net.Http.HttpClient" ||
                original.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                    "global::System.Net.Http.HttpMessageInvoker" ||
                original.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                    "global::System.Net.Http.HttpMessageHandler" ||
                original.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                    "global::System.Net.Http.DelegatingHandler");
    }

    private static bool ReceiverLooksLikeHttpClient(ExpressionSyntax receiver)
    {
        return receiver is IdentifierNameSyntax identifier &&
            RequestPathContext.VisibleIdentifierDeclarationType(identifier) is { } type &&
            type switch
            {
                IdentifierNameSyntax name => name.Identifier.ValueText is "HttpClient" or "HttpMessageInvoker",
                QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText is "HttpClient" or "HttpMessageInvoker",
                _ => false
            };
    }
}
