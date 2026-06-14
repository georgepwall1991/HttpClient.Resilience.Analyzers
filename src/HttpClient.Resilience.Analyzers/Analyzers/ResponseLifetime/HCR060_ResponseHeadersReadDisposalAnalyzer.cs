using System.Collections.Immutable;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.KnownSymbols;
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

            if (!IsResponseHeadersReadHttpCall(
                variable.Initializer.Value,
                context.SemanticModel,
                context.CancellationToken))
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

    private static bool IsResponseHeadersReadHttpCall(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return expression
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => IsResponseHeadersReadHttpCall(invocation, semanticModel, cancellationToken));
    }

    private static bool IsResponseHeadersReadHttpCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            !IsHttpResponseMethodName(memberAccess.Name.Identifier.ValueText) ||
            !IsHttpClientReceiver(memberAccess.Expression, semanticModel, cancellationToken))
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

    private static bool IsHttpClientReceiver(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (HttpClientSymbols.IsHttpClient(semanticModel.GetTypeInfo(expression, cancellationToken).Type))
        {
            return true;
        }

        return semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol switch
        {
            ILocalSymbol local => HttpClientSymbols.IsHttpClient(local.Type),
            IParameterSymbol parameter => HttpClientSymbols.IsHttpClient(parameter.Type),
            IFieldSymbol field => HttpClientSymbols.IsHttpClient(field.Type),
            IPropertySymbol property => HttpClientSymbols.IsHttpClient(property.Type),
            _ => false
        } || SyntacticReceiverLooksLikeHttpClient(expression);
    }

    private static bool SyntacticReceiverLooksLikeHttpClient(ExpressionSyntax expression)
    {
        return expression is IdentifierNameSyntax identifier &&
            (ParameterLooksLikeHttpClient(identifier) ||
                LocalLooksLikeHttpClient(identifier) ||
                FieldOrPropertyLooksLikeHttpClient(identifier));
    }

    private static bool ParameterLooksLikeHttpClient(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>()?
            .ParameterList.Parameters
            .Any(parameter => parameter.Identifier.ValueText == identifier.Identifier.ValueText &&
                parameter.Type is not null &&
                HttpClientSymbols.IsHttpClientName(parameter.Type)) == true;
    }

    private static bool LocalLooksLikeHttpClient(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<BlockSyntax>()?
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText &&
                variable.Parent is VariableDeclarationSyntax declaration &&
                HttpClientSymbols.IsHttpClientName(declaration.Type)) == true;
    }

    private static bool FieldOrPropertyLooksLikeHttpClient(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<TypeDeclarationSyntax>()?
            .Members
            .Any(member => member switch
            {
                FieldDeclarationSyntax field => HttpClientSymbols.IsHttpClientName(field.Declaration.Type) &&
                    field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText),
                PropertyDeclarationSyntax property => HttpClientSymbols.IsHttpClientName(property.Type) &&
                    property.Identifier.ValueText == identifier.Identifier.ValueText,
                _ => false
            }) == true;
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
