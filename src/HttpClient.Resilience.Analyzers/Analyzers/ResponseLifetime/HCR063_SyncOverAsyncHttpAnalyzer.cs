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
public sealed class HCR063_SyncOverAsyncHttpAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] HttpAsyncMethodNames =
    {
        "CopyToAsync",
        "DeleteAsync",
        "GetAsync",
        "GetByteArrayAsync",
        "GetFromJsonAsync",
        "GetStreamAsync",
        "GetStringAsync",
        "LoadIntoBufferAsync",
        "PatchAsync",
        "PostAsync",
        "PutAsync",
        "ReadAsByteArrayAsync",
        "ReadFromJsonAsync",
        "ReadAsStreamAsync",
        "ReadAsStringAsync",
        "SendAsync"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR063);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        if (memberAccess.Name.Identifier.ValueText != "Result" ||
            !ExpressionIsHttpAsyncOperation(memberAccess.Expression, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR063,
            memberAccess.Name.GetLocation()));
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        if (memberAccess.Name.Identifier.ValueText == "Wait" &&
            ExpressionIsHttpAsyncOperation(memberAccess.Expression, context.SemanticModel, context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.HCR063,
                memberAccess.Name.GetLocation()));
            return;
        }

        if (memberAccess.Name.Identifier.ValueText == "GetResult" &&
            memberAccess.Expression is InvocationExpressionSyntax getAwaiterInvocation &&
            getAwaiterInvocation.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "GetAwaiter"
            } getAwaiterAccess &&
            ExpressionIsHttpAsyncOperation(getAwaiterAccess.Expression, context.SemanticModel, context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.HCR063,
                memberAccess.Name.GetLocation()));
        }
    }

    private static bool ExpressionIsHttpAsyncOperation(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        expression = UnwrapParentheses(expression);

        if (expression is InvocationExpressionSyntax invocation)
        {
            return IsHttpAsyncCall(invocation, semanticModel, cancellationToken);
        }

        return expression is IdentifierNameSyntax identifier &&
            LocalInitializerIsHttpAsyncOperation(identifier, semanticModel, cancellationToken);
    }

    private static bool LocalInitializerIsHttpAsyncOperation(
        IdentifierNameSyntax identifier,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (identifier.FirstAncestorOrSelf<BlockSyntax>() is not { } block ||
            semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol is not ILocalSymbol local)
        {
            return false;
        }

        return block
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable =>
                variable.SpanStart < identifier.SpanStart &&
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetDeclaredSymbol(variable, cancellationToken),
                    local) &&
                variable.Initializer?.Value is { } initializer &&
                !LocalIsReassignedBetween(block, local, variable.SpanStart, identifier.SpanStart, semanticModel, cancellationToken) &&
                ExpressionIsHttpAsyncOperation(initializer, semanticModel, cancellationToken));
    }

    private static bool IsHttpAsyncCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            !HttpAsyncMethodNames.Contains(memberAccess.Name.Identifier.ValueText, System.StringComparer.Ordinal))
        {
            return false;
        }

        return IsHttpClientReceiver(memberAccess.Expression, semanticModel, cancellationToken) ||
            IsHttpContentReceiver(memberAccess.Expression, semanticModel, cancellationToken);
    }

    private static bool IsHttpClientReceiver(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var expressionType = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        if (expressionType is not null && expressionType is not IErrorTypeSymbol)
        {
            return HttpClientSymbols.IsHttpClient(expressionType);
        }

        var symbolType = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null
        };

        if (symbolType is not null && symbolType is not IErrorTypeSymbol)
        {
            return HttpClientSymbols.IsHttpClient(symbolType);
        }

        return SyntacticReceiverLooksLikeHttpClient(expression);
    }

    private static bool IsHttpContentReceiver(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var expressionType = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        if (expressionType is not null && expressionType is not IErrorTypeSymbol)
        {
            return IsHttpContent(expressionType);
        }

        return expression is MemberAccessExpressionSyntax
        {
            Name.Identifier.ValueText: "Content"
        };
    }

    private static bool IsHttpContent(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::System.Net.Http.HttpContent";
    }

    private static bool LocalIsReassignedBetween(
        BlockSyntax block,
        ILocalSymbol local,
        int start,
        int end,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return block
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.SpanStart > start &&
                assignment.SpanStart < end &&
                assignment.Left is IdentifierNameSyntax identifier &&
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol,
                    local));
    }

    private static ExpressionSyntax UnwrapParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static bool SyntacticReceiverLooksLikeHttpClient(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => ParameterLooksLikeHttpClient(identifier) ||
                LocalLooksLikeHttpClient(identifier) ||
                FieldOrPropertyLooksLikeHttpClient(identifier),
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax name } =>
                FieldOrPropertyLooksLikeHttpClient(name),
            _ => false
        };
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
}
