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

            if (!IsResponseHeadersReadHttpCall(variable.Initializer.Value))
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

    private static bool IsResponseHeadersReadHttpCall(ExpressionSyntax expression)
    {
        return expression
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(IsResponseHeadersReadHttpCall);
    }

    private static bool IsResponseHeadersReadHttpCall(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            !IsHttpResponseMethodName(memberAccess.Name.Identifier.ValueText))
        {
            return false;
        }

        return invocation.ArgumentList.Arguments
            .Select(argument => UnwrapParentheses(argument.Expression))
            .OfType<MemberAccessExpressionSyntax>()
            .Any(memberAccess =>
                memberAccess.Name.Identifier.ValueText == "ResponseHeadersRead" &&
                memberAccess.Expression.ToString() == "HttpCompletionOption");
    }

    private static bool IsHttpResponseMethodName(string methodName)
    {
        return methodName is
            "DeleteAsync" or
            "GetAsync" or
            "PatchAsync" or
            "PostAsync" or
            "PutAsync" or
            "SendAsync";
    }

    private static ExpressionSyntax UnwrapParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
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
                TransfersResponseOwnership(returnStatement.Expression, variableName));
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

    private static bool TransfersResponseOwnership(ExpressionSyntax expression, string variableName)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == variableName,
            ParenthesizedExpressionSyntax parenthesized => TransfersResponseOwnership(parenthesized.Expression, variableName),
            ObjectCreationExpressionSyntax objectCreation => HasDirectResponseArgument(objectCreation.ArgumentList, variableName),
            ImplicitObjectCreationExpressionSyntax implicitObjectCreation => HasDirectResponseArgument(implicitObjectCreation.ArgumentList, variableName),
            _ => false
        };
    }

    private static bool HasDirectResponseArgument(ArgumentListSyntax? argumentList, string variableName)
    {
        return argumentList?.Arguments
            .Select(argument => argument.Expression)
            .OfType<IdentifierNameSyntax>()
            .Any(identifier => identifier.Identifier.ValueText == variableName) == true;
    }
}
