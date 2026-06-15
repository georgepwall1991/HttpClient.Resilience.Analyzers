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
public sealed class HCR081_HttpStreamDisposalAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR081);

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
            if (variable.Initializer?.Value is not { } initializer ||
                !InitializerMaterializesHttpStream(initializer, context.SemanticModel, context.CancellationToken))
            {
                continue;
            }

            if (OwnershipIsTransferredOrDisposed(variable))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.HCR081,
                variable.Identifier.GetLocation()));
        }
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        if (assignment.Left is not IdentifierNameSyntax identifier ||
            context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol is not ILocalSymbol ||
            !InitializerMaterializesHttpStream(assignment.Right, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        if (OwnershipIsTransferredOrDisposed(identifier.Identifier.ValueText, assignment))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR081,
            identifier.GetLocation()));
    }

    private static bool InitializerMaterializesHttpStream(
        ExpressionSyntax initializer,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        initializer = UnwrapParentheses(initializer);
        return initializer is AwaitExpressionSyntax awaitExpression &&
            awaitExpression.Expression
                .DescendantNodesAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .Any(invocation => IsHttpStreamCall(invocation, semanticModel, cancellationToken));
    }

    private static bool IsHttpStreamCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        return (memberAccess.Name.Identifier.ValueText == "GetStreamAsync" &&
                IsHttpClientReceiver(memberAccess.Expression, semanticModel, cancellationToken) &&
                ReturnsStreamLike(invocation, semanticModel, cancellationToken)) ||
            (memberAccess.Name.Identifier.ValueText == "ReadAsStreamAsync" &&
                IsHttpContentReceiver(memberAccess.Expression, semanticModel, cancellationToken) &&
                ReturnsStreamLike(invocation, semanticModel, cancellationToken));
    }

    private static bool ReturnsStreamLike(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method)
        {
            return IsStreamOrTask(method.ReturnType);
        }

        var invocationType = semanticModel.GetTypeInfo(invocation, cancellationToken).Type;
        return invocationType is null ||
            invocationType is IErrorTypeSymbol ||
            IsStreamOrTask(invocationType);
    }

    private static bool IsStreamOrTask(ITypeSymbol type)
    {
        return IsStream(type) ||
            type is INamedTypeSymbol namedType &&
            namedType.IsGenericType &&
            namedType.TypeArguments.Length == 1 &&
            IsTaskLike(namedType) &&
            IsStream(namedType.TypeArguments[0]);
    }

    private static bool IsTaskLike(INamedTypeSymbol type)
    {
        var fullName = type.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return fullName is
            "global::System.Threading.Tasks.Task<TResult>" or
            "global::System.Threading.Tasks.ValueTask<TResult>";
    }

    private static bool IsStream(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::System.IO.Stream";
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

    private static bool OwnershipIsTransferredOrDisposed(VariableDeclaratorSyntax variable)
    {
        var containingBlock = variable.FirstAncestorOrSelf<BlockSyntax>();
        return containingBlock is not null &&
            OwnershipIsTransferredOrDisposed(containingBlock, variable.Identifier.ValueText, variable.SpanStart);
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
                TransfersStreamOwnership(
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
                !IsVariableReassignedBetween(containingBlock, variableName, declarationStart, statement.SpanStart));
    }

    private static bool IsDisposedInFinally(BlockSyntax containingBlock, string variableName, int declarationStart)
    {
        return containingBlock
            .DescendantNodes()
            .OfType<FinallyClauseSyntax>()
            .Any(finallyClause => finallyClause.Block
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(invocation => IsDisposeInvocation(invocation, variableName) &&
                    !IsVariableReassignedBetween(containingBlock, variableName, declarationStart, invocation.SpanStart)));
    }

    private static bool IsDisposeInvocation(ExpressionSyntax expression, string variableName)
    {
        expression = UnwrapParentheses(expression);
        if (expression is AwaitExpressionSyntax awaitExpression)
        {
            expression = UnwrapParentheses(awaitExpression.Expression);
        }

        return expression is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax identifier,
                Name.Identifier.ValueText: "Dispose" or "DisposeAsync"
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

    private static bool TransfersStreamOwnership(
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
                LocalInitializerTransfersStreamOwnership(
                    containingBlock,
                    identifier.Identifier.ValueText,
                    variableName,
                    declarationStart),
            ParenthesizedExpressionSyntax parenthesized => TransfersStreamOwnership(
                parenthesized.Expression,
                variableName,
                containingBlock,
                declarationStart,
                evidenceStart),
            ObjectCreationExpressionSyntax objectCreation => ObjectCreationTransfersStreamOwnership(
                objectCreation,
                variableName) &&
                !IsVariableReassignedBetween(containingBlock, variableName, declarationStart, evidenceStart),
            ImplicitObjectCreationExpressionSyntax implicitObjectCreation => ObjectCreationTransfersStreamOwnership(
                implicitObjectCreation,
                variableName) &&
                !IsVariableReassignedBetween(containingBlock, variableName, declarationStart, evidenceStart),
            _ => false
        };
    }

    private static bool LocalInitializerTransfersStreamOwnership(
        BlockSyntax containingBlock,
        string localName,
        string streamVariableName,
        int streamDeclarationStart)
    {
        return containingBlock
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable => variable.Identifier.ValueText == localName &&
                variable.Initializer?.Value is { } initializer &&
                variable.SpanStart > streamDeclarationStart &&
                !IsVariableReassignedBetween(
                    containingBlock,
                    streamVariableName,
                    streamDeclarationStart,
                    variable.SpanStart) &&
                InitializerCreatesStreamOwner(initializer, streamVariableName));
    }

    private static bool InitializerCreatesStreamOwner(ExpressionSyntax expression, string streamVariableName)
    {
        return expression switch
        {
            ObjectCreationExpressionSyntax objectCreation => ObjectCreationTransfersStreamOwnership(
                objectCreation,
                streamVariableName),
            ImplicitObjectCreationExpressionSyntax implicitObjectCreation => ObjectCreationTransfersStreamOwnership(
                implicitObjectCreation,
                streamVariableName),
            ParenthesizedExpressionSyntax parenthesized => InitializerCreatesStreamOwner(parenthesized.Expression, streamVariableName),
            _ => false
        };
    }

    private static bool ObjectCreationTransfersStreamOwnership(
        BaseObjectCreationExpressionSyntax objectCreation,
        string variableName)
    {
        return HasDirectStreamArgument(objectCreation.ArgumentList, variableName) ||
            HasDirectStreamInitializer(objectCreation.Initializer, variableName);
    }

    private static bool HasDirectStreamArgument(ArgumentListSyntax? argumentList, string variableName)
    {
        return argumentList?.Arguments
            .Select(argument => argument.Expression)
            .OfType<IdentifierNameSyntax>()
            .Any(identifier => identifier.Identifier.ValueText == variableName) == true;
    }

    private static bool HasDirectStreamInitializer(InitializerExpressionSyntax? initializer, string variableName)
    {
        return initializer?.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .Select(assignment => assignment.Right)
            .OfType<IdentifierNameSyntax>()
            .Any(identifier => identifier.Identifier.ValueText == variableName) == true;
    }

    private static bool IsDirectVariableReference(ExpressionSyntax expression, string variableName)
    {
        expression = UnwrapParentheses(expression);

        return expression is IdentifierNameSyntax identifier &&
            identifier.Identifier.ValueText == variableName;
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

    private static ExpressionSyntax UnwrapParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }
}
