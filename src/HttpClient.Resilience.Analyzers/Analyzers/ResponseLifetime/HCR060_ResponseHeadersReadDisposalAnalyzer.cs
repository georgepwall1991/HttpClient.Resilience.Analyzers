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
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
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

            if (!InitializerMaterializesResponse(
                    variable.Initializer.Value,
                    context.SemanticModel,
                    context.CancellationToken) ||
                !IsResponseHeadersReadHttpCall(
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

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        if (assignment.Left is not IdentifierNameSyntax identifier ||
            context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol is not ILocalSymbol ||
            !InitializerMaterializesResponse(
                assignment.Right,
                context.SemanticModel,
                context.CancellationToken) ||
            !IsResponseHeadersReadHttpCall(
                assignment.Right,
                context.SemanticModel,
                context.CancellationToken))
        {
            return;
        }

        if (OwnershipIsTransferredOrDisposed(identifier.Identifier.ValueText, assignment))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR060,
            identifier.GetLocation()));
    }

    private static bool InitializerMaterializesResponse(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        expression = UnwrapParentheses(expression);
        if (expression is AwaitExpressionSyntax)
        {
            return true;
        }

        var expressionType = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        return expressionType is not null &&
                expressionType is not IErrorTypeSymbol &&
                IsHttpResponseMessage(expressionType) ||
            expression is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: "Send"
                }
            };
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
            !InvocationTargetsHttpClient(invocation, semanticModel, cancellationToken) ||
            !IsHttpClientReceiver(memberAccess.Expression, semanticModel, cancellationToken) ||
            !ReturnsHttpResponseMessage(invocation, semanticModel, cancellationToken))
        {
            return false;
        }

        return invocation.ArgumentList.Arguments
            .Any(argument => IsResponseHeadersReadCompletionOption(
                argument.Expression,
                semanticModel,
                cancellationToken));
    }

    private static bool InvocationTargetsHttpClient(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return MethodTargetsHttpClient(method);
        }

        var candidateMethods = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
        return candidateMethods.Length == 0 || candidateMethods.All(MethodTargetsHttpClient);
    }

    private static bool MethodTargetsHttpClient(IMethodSymbol method)
    {
        return (method.ReducedFrom ?? method).ContainingType
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::System.Net.Http.HttpClient";
    }

    private static bool IsHttpResponseMethodName(string methodName)
    {
        return methodName is
            "DeleteAsync" or
            "GetAsync" or
            "PatchAsync" or
            "PostAsync" or
            "PutAsync" or
            "Send" or
            "SendAsync";
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
        if (invocationType is not null && invocationType is not IErrorTypeSymbol)
        {
            return IsHttpResponseMessageOrTask(invocationType);
        }

        return true;
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

    private static bool IsResponseHeadersReadCompletionOption(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        expression = UnwrapParentheses(expression);

        var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
        if (symbol is IFieldSymbol field)
        {
            return field.Name == "ResponseHeadersRead" &&
                IsHttpCompletionOption(field.ContainingType);
        }

        var expressionType = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        if (expressionType is not null && expressionType is not IErrorTypeSymbol)
        {
            return false;
        }

        return SyntacticExpressionLooksLikeResponseHeadersRead(expression);
    }

    private static bool IsHttpCompletionOption(ITypeSymbol? type)
    {
        return type is not null &&
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::System.Net.Http.HttpCompletionOption";
    }

    private static bool SyntacticExpressionLooksLikeResponseHeadersRead(ExpressionSyntax expression)
    {
        return expression is MemberAccessExpressionSyntax
        {
            Name.Identifier.ValueText: "ResponseHeadersRead"
        } memberAccess && SyntacticExpressionLooksLikeHttpCompletionOption(memberAccess.Expression);
    }

    private static bool SyntacticExpressionLooksLikeHttpCompletionOption(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "HttpCompletionOption",
            MemberAccessExpressionSyntax memberAccess => memberAccess.ToString() == "System.Net.Http.HttpCompletionOption",
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText == "HttpCompletionOption",
            _ => false
        };
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

        return OwnershipIsTransferredOrDisposed(containingBlock, variableName, variable.SpanStart);
    }

    private static bool OwnershipIsTransferredOrDisposed(string variableName, AssignmentExpressionSyntax assignment)
    {
        var containingBlock = assignment.FirstAncestorOrSelf<BlockSyntax>();

        return containingBlock is not null &&
            OwnershipIsTransferredOrDisposed(containingBlock, variableName, assignment.SpanStart);
    }

    private static bool OwnershipIsTransferredOrDisposed(BlockSyntax containingBlock, string variableName, int ownershipStart)
    {
        return IsReturned(containingBlock, variableName, ownershipStart) ||
            IsExplicitlyDisposed(containingBlock, variableName, ownershipStart);
    }

    private static bool IsReturned(BlockSyntax containingBlock, string variableName, int declarationStart)
    {
        return containingBlock
            .DescendantNodes()
            .OfType<ReturnStatementSyntax>()
            .Any(returnStatement => returnStatement.Expression is not null &&
                TransfersResponseOwnership(
                    returnStatement.Expression,
                    variableName,
                    containingBlock,
                    declarationStart,
                    returnStatement.SpanStart));
    }

    private static bool IsExplicitlyDisposed(BlockSyntax containingBlock, string variableName, int declarationStart)
    {
        return IsDirectlyDisposedInBlock(containingBlock, variableName, declarationStart) ||
            IsDisposedInFinally(containingBlock, variableName, declarationStart) ||
            IsOwnedByUsingStatement(containingBlock, variableName, declarationStart) ||
            IsOwnedByUsingDeclaration(containingBlock, variableName, declarationStart);
    }

    private static bool IsDirectlyDisposedInBlock(BlockSyntax containingBlock, string variableName, int declarationStart)
    {
        return containingBlock.Statements
            .OfType<ExpressionStatementSyntax>()
            .Any(statement => IsDisposeInvocation(statement.Expression, variableName) &&
                !IsVariableReassignedBetween(containingBlock, variableName, declarationStart, statement.SpanStart)) ||
            IsAliasDirectlyDisposedInBlock(containingBlock, variableName, declarationStart);
    }

    private static bool IsAliasDirectlyDisposedInBlock(
        BlockSyntax containingBlock,
        string variableName,
        int declarationStart)
    {
        return IsAliasDisposedInBlock(
            containingBlock,
            variableName,
            declarationStart,
            (aliasName, aliasStart) => AliasIsDirectlyDisposed(containingBlock, aliasName, aliasStart));
    }

    private static bool IsAliasDisposedInBlock(
        BlockSyntax containingBlock,
        string variableName,
        int declarationStart,
        System.Func<string, int, bool> aliasIsDisposed)
    {
        foreach (var alias in containingBlock.Statements
            .OfType<LocalDeclarationStatementSyntax>()
            .Where(statement => statement.SpanStart > declarationStart)
            .SelectMany(statement => statement.Declaration.Variables)
            .Where(alias => alias.Initializer?.Value is { } initializer &&
                IsDirectVariableReference(initializer, variableName) &&
                !IsVariableReassignedBetween(
                    containingBlock,
                    variableName,
                    declarationStart,
                    alias.SpanStart)))
        {
            var aliasName = alias.Identifier.ValueText;
            if (aliasIsDisposed(aliasName, alias.SpanStart))
            {
                return true;
            }
        }

        foreach (var aliasAssignment in containingBlock.Statements
            .OfType<ExpressionStatementSyntax>()
            .Where(statement => statement.SpanStart > declarationStart)
            .Select(statement => statement.Expression)
            .OfType<AssignmentExpressionSyntax>()
            .Where(assignment => assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                assignment.Left is IdentifierNameSyntax aliasIdentifier &&
                IsDirectVariableReference(assignment.Right, variableName) &&
                containingBlock.Statements
                    .OfType<LocalDeclarationStatementSyntax>()
                    .Any(statement => statement.SpanStart < assignment.SpanStart &&
                        statement.Declaration.Variables.Any(variable =>
                            variable.Identifier.ValueText == aliasIdentifier.Identifier.ValueText)) &&
                !IsVariableReassignedBetween(
                    containingBlock,
                    variableName,
                    declarationStart,
                    assignment.SpanStart)))
        {
            var aliasName = ((IdentifierNameSyntax)aliasAssignment.Left).Identifier.ValueText;
            if (aliasIsDisposed(aliasName, aliasAssignment.SpanStart))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AliasIsDirectlyDisposed(
        BlockSyntax containingBlock,
        string aliasName,
        int aliasStart)
    {
        return containingBlock.Statements
            .OfType<ExpressionStatementSyntax>()
            .Any(statement => statement.SpanStart > aliasStart &&
                IsDisposeInvocation(statement.Expression, aliasName) &&
                !IsVariableReassignedBetween(
                    containingBlock,
                    aliasName,
                    aliasStart,
                    statement.SpanStart)) ||
            IsAliasDirectlyDisposedInBlock(containingBlock, aliasName, aliasStart);
    }

    private static bool IsDisposedInFinally(BlockSyntax containingBlock, string variableName, int declarationStart)
    {
        return AliasIsDisposedInFinally(containingBlock, variableName, declarationStart);
    }

    private static bool AliasIsDisposedInFinally(
        BlockSyntax containingBlock,
        string aliasName,
        int aliasStart)
    {
        return containingBlock
            .DescendantNodes()
            .OfType<FinallyClauseSyntax>()
            .Any(finallyClause => finallyClause.Block
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(invocation => invocation.SpanStart > aliasStart &&
                    IsDisposeInvocation(invocation, aliasName) &&
                    !IsVariableReassignedBetween(containingBlock, aliasName, aliasStart, invocation.SpanStart))) ||
            IsAliasDisposedInBlock(
                containingBlock,
                aliasName,
                aliasStart,
                (nestedAliasName, nestedAliasStart) =>
                    AliasIsDisposedInFinally(containingBlock, nestedAliasName, nestedAliasStart));
    }

    private static bool IsDisposeInvocation(ExpressionSyntax expression, string variableName)
    {
        expression = UnwrapParentheses(expression);

        return expression is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax identifier,
                Name.Identifier.ValueText: "Dispose"
            }
        } && identifier.Identifier.ValueText == variableName;
    }

    private static bool IsOwnedByUsingStatement(BlockSyntax containingBlock, string variableName, int declarationStart)
    {
        return containingBlock
            .DescendantNodes()
            .OfType<UsingStatementSyntax>()
            .Any(usingStatement => usingStatement.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == variableName &&
                !IsVariableReassignedBetween(containingBlock, variableName, declarationStart, usingStatement.SpanStart));
    }

    private static bool IsOwnedByUsingDeclaration(BlockSyntax containingBlock, string variableName, int declarationStart)
    {
        return containingBlock.Statements
            .OfType<LocalDeclarationStatementSyntax>()
            .Any(statement => statement.UsingKeyword.IsKind(SyntaxKind.UsingKeyword) &&
                statement.SpanStart > declarationStart &&
                statement.Declaration.Variables.Any(variable => variable.Initializer?.Value is { } initializer &&
                    IsDirectVariableReference(initializer, variableName)) &&
                !IsVariableReassignedBetween(containingBlock, variableName, declarationStart, statement.SpanStart));
    }

    private static bool IsDirectVariableReference(ExpressionSyntax expression, string variableName)
    {
        expression = UnwrapParentheses(expression);

        return expression is IdentifierNameSyntax identifier &&
            identifier.Identifier.ValueText == variableName;
    }

    private static bool TransfersResponseOwnership(
        ExpressionSyntax expression,
        string variableName,
        BlockSyntax containingBlock,
        int declarationStart,
        int evidenceStart)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == variableName &&
                !IsVariableReassignedBetween(containingBlock, variableName, declarationStart, evidenceStart) ||
                LocalInitializerTransfersResponseOwnership(
                    containingBlock,
                    identifier.Identifier.ValueText,
                    variableName,
                    declarationStart),
            ParenthesizedExpressionSyntax parenthesized => TransfersResponseOwnership(
                parenthesized.Expression,
                variableName,
                containingBlock,
                declarationStart,
                evidenceStart),
            ObjectCreationExpressionSyntax objectCreation => ObjectCreationTransfersResponseOwnership(
                objectCreation,
                variableName) &&
                !IsVariableReassignedBetween(containingBlock, variableName, declarationStart, evidenceStart),
            ImplicitObjectCreationExpressionSyntax implicitObjectCreation => ObjectCreationTransfersResponseOwnership(
                implicitObjectCreation,
                variableName) &&
                !IsVariableReassignedBetween(containingBlock, variableName, declarationStart, evidenceStart),
            _ => false
        };
    }

    private static bool LocalInitializerTransfersResponseOwnership(
        BlockSyntax containingBlock,
        string localName,
        string responseVariableName,
        int responseDeclarationStart)
    {
        return containingBlock
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable => variable.Identifier.ValueText == localName &&
                variable.Initializer?.Value is { } initializer &&
                variable.SpanStart > responseDeclarationStart &&
                !IsVariableReassignedBetween(
                    containingBlock,
                    responseVariableName,
                    responseDeclarationStart,
                    variable.SpanStart) &&
                InitializerCreatesResponseOwner(initializer, responseVariableName));
    }

    private static bool InitializerCreatesResponseOwner(ExpressionSyntax expression, string responseVariableName)
    {
        return expression switch
        {
            ObjectCreationExpressionSyntax objectCreation => ObjectCreationTransfersResponseOwnership(
                objectCreation,
                responseVariableName),
            ImplicitObjectCreationExpressionSyntax implicitObjectCreation => ObjectCreationTransfersResponseOwnership(
                implicitObjectCreation,
                responseVariableName),
            ParenthesizedExpressionSyntax parenthesized => InitializerCreatesResponseOwner(parenthesized.Expression, responseVariableName),
            _ => false
        };
    }

    private static bool ObjectCreationTransfersResponseOwnership(
        BaseObjectCreationExpressionSyntax objectCreation,
        string variableName)
    {
        return HasDirectResponseArgument(objectCreation.ArgumentList, variableName) ||
            HasDirectResponseInitializer(objectCreation.Initializer, variableName);
    }

    private static bool HasDirectResponseArgument(ArgumentListSyntax? argumentList, string variableName)
    {
        return argumentList?.Arguments
            .Select(argument => argument.Expression)
            .OfType<IdentifierNameSyntax>()
            .Any(identifier => identifier.Identifier.ValueText == variableName) == true;
    }

    private static bool HasDirectResponseInitializer(InitializerExpressionSyntax? initializer, string variableName)
    {
        return initializer?.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .Select(assignment => assignment.Right)
            .OfType<IdentifierNameSyntax>()
            .Any(identifier => identifier.Identifier.ValueText == variableName) == true;
    }

    private static bool IsVariableReassignedBetween(
        BlockSyntax containingBlock,
        string variableName,
        int start,
        int end)
    {
        return containingBlock
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.SpanStart > start &&
                assignment.SpanStart < end &&
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                assignment.Left is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == variableName);
    }
}
