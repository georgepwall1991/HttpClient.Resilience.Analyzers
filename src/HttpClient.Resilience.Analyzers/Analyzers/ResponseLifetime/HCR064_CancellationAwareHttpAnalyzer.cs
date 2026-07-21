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
public sealed class HCR064_CancellationAwareHttpAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] HttpClientMethodNames =
    {
        "DeleteAsync",
        "GetAsync",
        "GetByteArrayAsync",
        "GetStreamAsync",
        "GetStringAsync",
        "PatchAsync",
        "PostAsync",
        "PutAsync",
        "Send",
        "SendAsync"
    };

    private static readonly string[] HttpContentMethodNames =
    {
        "CopyToAsync",
        "LoadIntoBufferAsync",
        "ReadAsByteArrayAsync",
        "ReadFromJsonAsAsyncEnumerable",
        "ReadFromJsonAsync",
        "ReadAsStream",
        "ReadAsStreamAsync",
        "ReadAsStringAsync"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR064);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            InvocationAlreadyPassesCancellationToken(invocation, context.SemanticModel, context.CancellationToken) ||
            !VisibleCancellationTokenExists(invocation, context.SemanticModel, context.CancellationToken) ||
            !IsCancellationAwareHttpCall(invocation, memberAccess, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR064,
            memberAccess.Name.GetLocation()));
    }

    private static bool IsCancellationAwareHttpCall(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return (HttpClientMethodNames.Contains(memberAccess.Name.Identifier.ValueText, System.StringComparer.Ordinal) &&
                IsHttpClientReceiver(memberAccess.Expression, semanticModel, cancellationToken) &&
                MethodHasCancellationTokenOverload(invocation, semanticModel, cancellationToken)) ||
            (HttpContentMethodNames.Contains(memberAccess.Name.Identifier.ValueText, System.StringComparer.Ordinal) &&
                IsHttpContentReceiver(memberAccess.Expression, semanticModel, cancellationToken) &&
                MethodHasCancellationTokenOverload(invocation, semanticModel, cancellationToken));
    }

    private static bool MethodHasCancellationTokenOverload(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method)
        {
            return true;
        }

        return method.ContainingType
            .GetMembers(method.Name)
            .OfType<IMethodSymbol>()
            .Any(candidate => candidate.Parameters.Any(parameter => IsCancellationToken(parameter.Type)));
    }

    private static bool InvocationAlreadyPassesCancellationToken(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return invocation.ArgumentList.Arguments
            .Select(argument => argument.Expression)
            .Any(expression => IsCancellationTokenExpression(expression, semanticModel, cancellationToken) &&
                !IsCancellationTokenNone(expression, semanticModel, cancellationToken));
    }

    private static bool IsCancellationTokenNone(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (expression is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "None"
            } memberAccess)
        {
            return false;
        }

        if (semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol is IPropertySymbol property)
        {
            return IsCancellationToken(property.ContainingType) && IsCancellationToken(property.Type);
        }

        return memberAccess.Expression.ToString().EndsWith("CancellationToken", System.StringComparison.Ordinal);
    }

    private static bool VisibleCancellationTokenExists(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return ScopeHasVisibleCancellationTokenSymbol(invocation, semanticModel) ||
            EnclosingParameterListHasCancellationToken(invocation, semanticModel, cancellationToken) ||
            EnclosingLambdaParameterListHasCancellationToken(invocation, semanticModel, cancellationToken) ||
            BlockHasPriorCancellationTokenLocal(invocation, semanticModel, cancellationToken);
    }

    private static bool ScopeHasVisibleCancellationTokenSymbol(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        return semanticModel.LookupSymbols(invocation.SpanStart)
            .Any(symbol => symbol switch
            {
                ILocalSymbol local => IsCancellationToken(local.Type) || IsCancellationTokenSource(local.Type),
                IParameterSymbol parameter => IsCancellationToken(parameter.Type) || IsCancellationTokenSource(parameter.Type),
                _ => false
            });
    }

    private static bool EnclosingParameterListHasCancellationToken(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return invocation.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>()?
            .ParameterList.Parameters
            .Any(parameter => parameter.SpanStart < invocation.SpanStart &&
                ParameterIsCancellationToken(parameter, semanticModel, cancellationToken)) == true;
    }

    private static bool EnclosingLambdaParameterListHasCancellationToken(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return invocation.Ancestors()
            .OfType<LambdaExpressionSyntax>()
            .Any(lambda => lambda.SpanStart < invocation.SpanStart &&
                lambda switch
                {
                    ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters
                        .Any(parameter => ParameterIsCancellationToken(parameter, semanticModel, cancellationToken)),
                    SimpleLambdaExpressionSyntax simple => ParameterIsCancellationToken(simple.Parameter, semanticModel, cancellationToken),
                    _ => false
                });
    }

    private static bool BlockHasPriorCancellationTokenLocal(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return invocation.FirstAncestorOrSelf<BlockSyntax>()?
            .Statements
            .TakeWhile(statement => statement.SpanStart < invocation.SpanStart)
            .OfType<LocalDeclarationStatementSyntax>()
            .SelectMany(statement => statement.Declaration.Variables)
            .Any(variable =>
                semanticModel.GetDeclaredSymbol(variable, cancellationToken) is ILocalSymbol local &&
                IsCancellationToken(local.Type)) == true;
    }

    private static bool ParameterIsCancellationToken(
        ParameterSyntax parameter,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return semanticModel.GetDeclaredSymbol(parameter, cancellationToken) is IParameterSymbol parameterSymbol &&
            IsCancellationToken(parameterSymbol.Type) ||
            parameter.Type is not null &&
            TypeSyntaxLooksLikeCancellationToken(parameter.Type);
    }

    private static bool IsCancellationTokenExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var expressionType = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        if (expressionType is not null && expressionType is not IErrorTypeSymbol)
        {
            return IsCancellationToken(expressionType);
        }

        return (expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText is "cancellationToken" or "ct") ||
            (expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.ValueText is "None" or "CancellationToken");
    }

    private static bool IsCancellationToken(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::System.Threading.CancellationToken";
    }

    private static bool IsCancellationTokenSource(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::System.Threading.CancellationTokenSource";
    }

    private static bool TypeSyntaxLooksLikeCancellationToken(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "CancellationToken",
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText == "CancellationToken",
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText == "CancellationToken",
            _ => false
        };
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
