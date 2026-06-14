using System.Collections.Immutable;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.ResponseLifetime;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR060_ResponseHeadersReadDisposalAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR060);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeLocalDeclaration, SyntaxKind.LocalDeclarationStatement);
    }

    private static void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext context)
    {
        var declaration = (LocalDeclarationStatementSyntax)context.Node;

        if (declaration.UsingKeyword != default)
        {
            return;
        }

        foreach (var variable in declaration.Declaration.Variables)
        {
            if (variable.Initializer is null)
            {
                continue;
            }

            if (!ContainsResponseHeadersRead(variable.Initializer.Value))
            {
                continue;
            }

            if (OwnershipIsTransferredOrDisposed(variable))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.HCR060,
                variable.Identifier.GetLocation()));
        }
    }

    private static bool ContainsResponseHeadersRead(ExpressionSyntax expression)
    {
        return expression
            .DescendantNodesAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .Any(memberAccess =>
                memberAccess.Name.Identifier.ValueText == "ResponseHeadersRead" &&
                memberAccess.Expression.ToString() == "HttpCompletionOption");
    }

    private static bool OwnershipIsTransferredOrDisposed(VariableDeclaratorSyntax variable)
    {
        var variableName = variable.Identifier.ValueText;
        var containingBlock = variable.FirstAncestorOrSelf<BlockSyntax>();

        if (containingBlock is null)
        {
            return false;
        }

        return IsReturned(containingBlock, variableName) ||
            IsExplicitlyDisposed(containingBlock, variableName);
    }

    private static bool IsReturned(BlockSyntax containingBlock, string variableName)
    {
        return containingBlock
            .DescendantNodes()
            .OfType<ReturnStatementSyntax>()
            .Any(returnStatement => returnStatement.Expression is not null &&
                ContainsIdentifier(returnStatement.Expression, variableName));
    }

    private static bool IsExplicitlyDisposed(BlockSyntax containingBlock, string variableName)
    {
        return containingBlock
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => invocation.Expression is MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax identifier,
                Name.Identifier.ValueText: "Dispose"
            } && identifier.Identifier.ValueText == variableName);
    }

    private static bool ContainsIdentifier(SyntaxNode node, string variableName)
    {
        return node
            .DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(identifier => identifier.Identifier.ValueText == variableName);
    }
}
