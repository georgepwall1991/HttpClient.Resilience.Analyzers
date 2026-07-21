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
public sealed class HCR061_UnsuccessfulResponseIgnoredAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] HttpResponseMethodNames =
    {
        "DeleteAsync",
        "GetAsync",
        "PatchAsync",
        "PostAsync",
        "PutAsync",
        "SendAsync"
    };

    private static readonly string[] ContentReadMethodNames =
    {
        "CopyToAsync",
        "LoadIntoBufferAsync",
        "ReadAsByteArrayAsync",
        "ReadFromJsonAsync",
        "ReadAsStreamAsync",
        "ReadAsStringAsync"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR061);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeLocalDeclaration, SyntaxKind.LocalDeclarationStatement);
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
    }

    private static void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext context)
    {
        var declaration = (LocalDeclarationStatementSyntax)context.Node;
        foreach (var variable in declaration.Declaration.Variables)
        {
            if (variable.Initializer?.Value is not { } initializer ||
                !InitializerAwaitsHttpResponse(initializer, context.SemanticModel, context.CancellationToken) ||
                context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken) is not ILocalSymbol responseLocal)
            {
                continue;
            }

            if (FindFirstContentRead(variable, responseLocal, context.SemanticModel, context.CancellationToken) is not { } contentRead)
            {
                continue;
            }

            if (HasSuccessCheckBefore(variable, responseLocal, contentRead.SpanStart, context.SemanticModel, context.CancellationToken))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.HCR061,
                variable.Identifier.GetLocation()));
        }
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        if (assignment.Parent is not ExpressionStatementSyntax ||
            assignment.Left is not IdentifierNameSyntax responseIdentifier ||
            !InitializerAwaitsHttpResponse(assignment.Right, context.SemanticModel, context.CancellationToken) ||
            context.SemanticModel.GetSymbolInfo(responseIdentifier, context.CancellationToken).Symbol is not ILocalSymbol responseLocal)
        {
            return;
        }

        if (FindFirstContentRead(assignment, responseLocal, context.SemanticModel, context.CancellationToken) is not { } contentRead ||
            HasSuccessCheckBefore(assignment, responseLocal, contentRead.SpanStart, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR061,
            responseIdentifier.GetLocation()));
    }

    private static bool InitializerAwaitsHttpResponse(
        ExpressionSyntax initializer,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        initializer = UnwrapParentheses(initializer);
        return initializer is AwaitExpressionSyntax awaitExpression &&
            awaitExpression.Expression
                .DescendantNodesAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .Any(invocation => IsHttpResponseCall(invocation, semanticModel, cancellationToken));
    }

    private static bool IsHttpResponseCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            HttpResponseMethodNames.Contains(memberAccess.Name.Identifier.ValueText, System.StringComparer.Ordinal) &&
            IsHttpClientReceiver(memberAccess.Expression, semanticModel, cancellationToken) &&
            ReturnsHttpResponseMessage(invocation, semanticModel, cancellationToken);
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

    private static bool ReturnsHttpResponseMessage(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method)
        {
            return IsHttpResponseMessageOrTask(method.ReturnType);
        }

        var invocationType = semanticModel.GetTypeInfo(invocation, cancellationToken).Type;
        return invocationType is null ||
            invocationType is IErrorTypeSymbol ||
            IsHttpResponseMessageOrTask(invocationType);
    }

    private static bool IsHttpResponseMessageOrTask(ITypeSymbol type)
    {
        return IsHttpResponseMessage(type) ||
            type is INamedTypeSymbol namedType &&
            namedType.IsGenericType &&
            namedType.TypeArguments.Length == 1 &&
            IsTaskLike(namedType) &&
            IsHttpResponseMessage(namedType.TypeArguments[0]);
    }

    private static bool IsTaskLike(INamedTypeSymbol type)
    {
        var fullName = type.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return fullName is
            "global::System.Threading.Tasks.Task<TResult>" or
            "global::System.Threading.Tasks.ValueTask<TResult>";
    }

    private static bool IsHttpResponseMessage(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::System.Net.Http.HttpResponseMessage";
    }

    private static InvocationExpressionSyntax? FindFirstContentRead(
        SyntaxNode origin,
        ILocalSymbol responseLocal,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (origin.FirstAncestorOrSelf<BlockSyntax>() is not { } block)
        {
            return null;
        }

        foreach (var invocation in block
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.SpanStart > origin.SpanStart)
            .OrderBy(invocation => invocation.SpanStart))
        {
            if (IsContentRead(invocation, responseLocal, semanticModel, cancellationToken))
            {
                if (LocalIsReassignedBetween(block, responseLocal, origin.SpanStart, invocation.SpanStart, semanticModel, cancellationToken))
                {
                    return null;
                }

                return invocation;
            }
        }

        return null;
    }

    private static bool IsContentRead(
        InvocationExpressionSyntax invocation,
        ILocalSymbol responseLocal,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return invocation.Expression is MemberAccessExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Content",
                Expression: IdentifierNameSyntax responseIdentifier
            },
            Name: SimpleNameSyntax methodName
        } &&
            ContentReadMethodNames.Contains(methodName.Identifier.ValueText, System.StringComparer.Ordinal) &&
            SymbolEqualityComparer.Default.Equals(
                semanticModel.GetSymbolInfo(responseIdentifier, cancellationToken).Symbol,
                responseLocal);
    }

    private static bool HasSuccessCheckBefore(
        SyntaxNode origin,
        ILocalSymbol responseLocal,
        int contentReadStart,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (origin.FirstAncestorOrSelf<BlockSyntax>() is not { } block)
        {
            return false;
        }

        return block
            .DescendantNodes()
            .Where(node => node.SpanStart > origin.SpanStart && node.SpanStart < contentReadStart)
            .Any(node => node switch
            {
                InvocationExpressionSyntax invocation => IsEnsureSuccessStatusCodeCall(
                    invocation,
                    responseLocal,
                    semanticModel,
                    cancellationToken),
                MemberAccessExpressionSyntax memberAccess => IsSuccessStatusCheck(
                    memberAccess,
                    responseLocal,
                    semanticModel,
                    cancellationToken),
                _ => false
            });
    }

    private static bool IsEnsureSuccessStatusCodeCall(
        InvocationExpressionSyntax invocation,
        ILocalSymbol responseLocal,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return invocation.Expression is MemberAccessExpressionSyntax
        {
            Name.Identifier.ValueText: "EnsureSuccessStatusCode",
            Expression: IdentifierNameSyntax responseIdentifier
        } &&
            SymbolEqualityComparer.Default.Equals(
                semanticModel.GetSymbolInfo(responseIdentifier, cancellationToken).Symbol,
                responseLocal);
    }

    private static bool IsSuccessStatusCheck(
        MemberAccessExpressionSyntax memberAccess,
        ILocalSymbol responseLocal,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return memberAccess.Name.Identifier.ValueText is "IsSuccessStatusCode" or "StatusCode" &&
            memberAccess.Expression is IdentifierNameSyntax responseIdentifier &&
            SymbolEqualityComparer.Default.Equals(
                semanticModel.GetSymbolInfo(responseIdentifier, cancellationToken).Symbol,
                responseLocal);
    }

    private static bool LocalIsReassignedBetween(
        BlockSyntax block,
        ILocalSymbol responseLocal,
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
                    responseLocal));
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
