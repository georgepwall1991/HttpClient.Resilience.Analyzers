using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.KnownSymbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace HttpClient.Resilience.Analyzers.Analyzers.Concurrency;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR080_UnboundedHttpFanOutAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] HttpCallMethodNames =
    {
        "DeleteAsync",
        "DeleteFromJsonAsync",
        "GetAsync",
        "GetByteArrayAsync",
        "GetFromJsonAsync",
        "GetStreamAsync",
        "GetStringAsync",
        "PatchAsJsonAsync",
        "PatchAsync",
        "PostAsJsonAsync",
        "PostAsync",
        "PutAsJsonAsync",
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
        if (!IsTaskWhenAll(invocation, context.SemanticModel, context.CancellationToken) ||
            invocation.ArgumentList.Arguments.Count == 0)
        {
            return;
        }

        if (!ContainsSelectWithHttpCall(
            invocation.ArgumentList.Arguments[0].Expression,
            context.SemanticModel,
            context.CancellationToken))
        {
            return;
        }

        var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR080,
            memberAccess.Name.GetLocation()));
    }

    private static bool IsTaskWhenAll(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return IsBclTaskWhenAll(method);
        }

        if (symbolInfo.CandidateSymbols.Length > 0)
        {
            return symbolInfo.CandidateSymbols
                .OfType<IMethodSymbol>()
                .Any(IsBclTaskWhenAll);
        }

        return IsSyntacticTaskWhenAll(invocation);
    }

    private static bool IsBclTaskWhenAll(IMethodSymbol method)
    {
        return method.Name == "WhenAll" &&
            method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::System.Threading.Tasks.Task";
    }

    private static bool IsSyntacticTaskWhenAll(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.ValueText: "Task" } or
                MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Task" },
            Name.Identifier.ValueText: "WhenAll"
        };
    }

    private static bool ContainsSelectWithHttpCall(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return ContainsSelectWithHttpCall(
            expression,
            semanticModel,
            cancellationToken,
            ImmutableHashSet.Create<ISymbol>(SymbolEqualityComparer.Default));
    }

    private static bool ContainsSelectWithHttpCall(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        ImmutableHashSet<ISymbol> visitedLocals)
    {
        expression = UnwrapParentheses(expression);

        if (ExpressionContainsSelectWithHttpCall(expression, semanticModel, cancellationToken))
        {
            return true;
        }

        return expression is IdentifierNameSyntax identifier &&
            LocalValueContainsSelectWithHttpCall(
                identifier,
                semanticModel,
                cancellationToken,
                visitedLocals);
    }

    private static bool ExpressionContainsSelectWithHttpCall(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return expression
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => IsSelectInvocationWithHttpCall(invocation, semanticModel, cancellationToken)) ||
            expression
                .DescendantNodesAndSelf()
                .OfType<QueryExpressionSyntax>()
                .Any(query => IsLinqQueryWithHttpCall(query, semanticModel, cancellationToken));
    }

    private static bool IsLinqQueryWithHttpCall(
        QueryExpressionSyntax query,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (query.Body.SelectOrGroup is not SelectClauseSyntax selectClause ||
            semanticModel.GetOperation(query, cancellationToken) is not { } operation ||
            !operation.DescendantsAndSelf()
                .OfType<IInvocationOperation>()
                .Any(invocation => IsLinqSelect(invocation.TargetMethod)))
        {
            return false;
        }

        return selectClause.Expression
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => IsUnboundedHttpCall(invocation, semanticModel, cancellationToken));
    }

    private static bool LocalValueContainsSelectWithHttpCall(
        IdentifierNameSyntax identifier,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        ImmutableHashSet<ISymbol> visitedLocals)
    {
        var containingBlock = identifier.FirstAncestorOrSelf<BlockSyntax>();
        if (containingBlock is null ||
            semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol is not ILocalSymbol local ||
            visitedLocals.Contains(local))
        {
            return false;
        }

        var declaration = containingBlock
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(variable =>
                variable.SpanStart < identifier.SpanStart &&
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetDeclaredSymbol(variable, cancellationToken),
                    local));
        if (declaration is null)
        {
            return false;
        }

        var latestAssignment = containingBlock
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(assignment => assignment.SpanStart > declaration.SpanStart &&
                assignment.SpanStart < identifier.SpanStart &&
                assignment.Left is IdentifierNameSyntax assignmentIdentifier &&
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(assignmentIdentifier, cancellationToken).Symbol,
                    local))
            .OrderByDescending(assignment => assignment.SpanStart)
            .FirstOrDefault();

        ExpressionSyntax? value;
        if (latestAssignment is null)
        {
            value = declaration.Initializer?.Value;
        }
        else if (latestAssignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
            latestAssignment.Parent is ExpressionStatementSyntax { Parent: BlockSyntax assignmentBlock } &&
            assignmentBlock == containingBlock)
        {
            value = latestAssignment.Right;
        }
        else
        {
            return false;
        }

        return value is not null &&
            ContainsSelectWithHttpCall(
                value,
                semanticModel,
                cancellationToken,
                visitedLocals.Add(local));
    }

    private static bool IsSelectInvocationWithHttpCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Select"
            })
        {
            return false;
        }

        if (!IsLinqSelectInvocation(invocation, semanticModel, cancellationToken))
        {
            return false;
        }

        return invocation.ArgumentList.Arguments
            .Select(argument => argument.Expression)
            .OfType<LambdaExpressionSyntax>()
            .Any(lambda => !UsesSemaphoreGate(lambda, semanticModel, cancellationToken) &&
                lambda.Body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(
                    invocation => IsUnboundedHttpCall(invocation, semanticModel, cancellationToken)));
    }

    private static bool IsLinqSelectInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return IsLinqSelect(method);
        }

        if (symbolInfo.CandidateSymbols.Length > 0)
        {
            return symbolInfo.CandidateSymbols
                .OfType<IMethodSymbol>()
                .Any(IsLinqSelect);
        }

        return true;
    }

    private static bool IsLinqSelect(IMethodSymbol method)
    {
        return method.Name == "Select" &&
            method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) is
                "global::System.Linq.Enumerable" or
                "global::System.Linq.Queryable";
    }

    private static bool IsHttpCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            HttpCallMethodNames.Contains(memberAccess.Name.Identifier.ValueText, System.StringComparer.Ordinal) &&
            InvocationTargetsHttpClient(invocation, semanticModel, cancellationToken) &&
            IsHttpClientReceiver(memberAccess.Expression, semanticModel, cancellationToken);
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
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) is
            "global::System.Net.Http.HttpClient" or
            "global::System.Net.Http.Json.HttpClientJsonExtensions";
    }

    private static bool IsUnboundedHttpCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return IsHttpCall(invocation, semanticModel, cancellationToken) &&
            !UsesConnectionLimitedClient(invocation, semanticModel, cancellationToken);
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
                (HttpClientSymbols.IsHttpClientName(declaration.Type) ||
                    variable.Initializer?.Value is BaseObjectCreationExpressionSyntax creation &&
                    IsHttpClientCreation(creation))) == true;
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

    private static bool UsesConnectionLimitedClient(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            !TryGetHttpClientReceiverIdentifier(memberAccess.Expression, out var receiver))
        {
            return false;
        }

        var scope = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>() as SyntaxNode ??
            invocation.SyntaxTree.GetRoot();
        var declarations = scope
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .ToArray();
        var clientDeclaration = declarations
            .FirstOrDefault(declaration => declaration.Identifier.ValueText == receiver.Identifier.ValueText);

        if (clientDeclaration?.Initializer?.Value is { } value &&
            !IsLocalReassignedBetween(clientDeclaration, invocation.SpanStart) &&
            IsConnectionLimitedHttpClientCreation(
                value,
                declarations,
                clientDeclaration.SpanStart,
                semanticModel,
                cancellationToken))
        {
            return true;
        }

        return ReceiverMemberUsesConnectionLimitedHandler(receiver, semanticModel, cancellationToken);
    }

    private static bool TryGetHttpClientReceiverIdentifier(
        ExpressionSyntax expression,
        out IdentifierNameSyntax receiver)
    {
        switch (expression)
        {
            case IdentifierNameSyntax identifier:
                receiver = identifier;
                return true;
            case MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax name }:
                receiver = name;
                return true;
            default:
                receiver = null!;
                return false;
        }
    }

    private static bool ReceiverMemberUsesConnectionLimitedHandler(
        IdentifierNameSyntax receiver,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var typeMemberHandlers = GetTypeMemberHandlerDeclarations(receiver);

        return receiver.FirstAncestorOrSelf<TypeDeclarationSyntax>()?
            .Members
            .Any(member => member switch
            {
                FieldDeclarationSyntax field => HttpClientSymbols.IsHttpClientName(field.Declaration.Type) &&
                    field.Declaration.Variables.Any(variable =>
                        variable.Identifier.ValueText == receiver.Identifier.ValueText &&
                        variable.Initializer?.Value is { } value &&
                        IsConnectionLimitedHttpClientCreation(
                            value,
                            typeMemberHandlers,
                            variable.SpanStart,
                            semanticModel,
                            cancellationToken)),
                PropertyDeclarationSyntax property => HttpClientSymbols.IsHttpClientName(property.Type) &&
                    property.Identifier.ValueText == receiver.Identifier.ValueText &&
                    property.Initializer?.Value is { } value &&
                    IsConnectionLimitedHttpClientCreation(
                        value,
                        typeMemberHandlers,
                        property.SpanStart,
                        semanticModel,
                        cancellationToken),
                _ => false
            }) == true;
    }

    private static IReadOnlyCollection<VariableDeclaratorSyntax> GetTypeMemberHandlerDeclarations(SyntaxNode node)
    {
        return node.FirstAncestorOrSelf<TypeDeclarationSyntax>()?
            .Members
            .OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables)
            .ToArray() ?? System.Array.Empty<VariableDeclaratorSyntax>();
    }

    private static bool IsConnectionLimitedHttpClientCreation(
        ExpressionSyntax expression,
        IReadOnlyCollection<VariableDeclaratorSyntax> declarations,
        int evidenceStart,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (expression is not BaseObjectCreationExpressionSyntax creation ||
            !IsHttpClientCreation(creation) ||
            creation.ArgumentList is not { Arguments.Count: > 0 } argumentList)
        {
            return false;
        }

        return argumentList.Arguments
            .Select(argument => argument.Expression)
            .Any(argument => IsConnectionLimitedHandler(
                argument,
                declarations,
                evidenceStart,
                semanticModel,
                cancellationToken));
    }

    private static bool IsHttpClientCreation(BaseObjectCreationExpressionSyntax creation)
    {
        return creation is ImplicitObjectCreationExpressionSyntax ||
            creation is ObjectCreationExpressionSyntax objectCreation &&
            objectCreation.Type.ToString().EndsWith("HttpClient", System.StringComparison.Ordinal);
    }

    private static bool IsConnectionLimitedHandler(
        ExpressionSyntax expression,
        IReadOnlyCollection<VariableDeclaratorSyntax> declarations,
        int evidenceStart,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (expression is BaseObjectCreationExpressionSyntax creation)
        {
            return IsFrameworkConnectionLimitedHandlerCreation(creation, semanticModel, cancellationToken) &&
                HasMaxConnectionsPerServerInitializer(creation);
        }

        if (expression is not IdentifierNameSyntax identifier)
        {
            return false;
        }

        var handlerDeclaration = declarations
            .FirstOrDefault(declaration => declaration.Identifier.ValueText == identifier.Identifier.ValueText);

        return handlerDeclaration?.Initializer?.Value is BaseObjectCreationExpressionSyntax handlerCreation &&
            IsFrameworkConnectionLimitedHandlerCreation(
                handlerCreation,
                semanticModel,
                cancellationToken,
                handlerDeclaration) &&
            !IsLocalReassignedBetween(handlerDeclaration, evidenceStart) &&
            HasMaxConnectionsPerServerInitializer(handlerCreation);
    }

    private static bool IsLocalReassignedBetween(VariableDeclaratorSyntax variable, int evidenceStart)
    {
        var variableName = variable.Identifier.ValueText;

        return variable.FirstAncestorOrSelf<BlockSyntax>()?
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.SpanStart > variable.SpanStart &&
                assignment.SpanStart < evidenceStart &&
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                assignment.Left is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == variableName) == true;
    }

    private static bool IsFrameworkConnectionLimitedHandlerCreation(
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        VariableDeclaratorSyntax? variable = null)
    {
        var creationType = semanticModel.GetTypeInfo(creation, cancellationToken).Type;
        if (creationType is not null && creationType is not IErrorTypeSymbol)
        {
            var fullyQualifiedType = creationType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return fullyQualifiedType == "global::System.Net.Http.SocketsHttpHandler" ||
                fullyQualifiedType == "global::System.Net.Http.HttpClientHandler";
        }

        return creation is ObjectCreationExpressionSyntax objectCreation &&
            IsFrameworkConnectionLimitedHandlerName(objectCreation.Type.ToString()) ||
            creation is ImplicitObjectCreationExpressionSyntax &&
            variable?.Parent is VariableDeclarationSyntax declaration &&
            IsFrameworkConnectionLimitedHandlerName(declaration.Type.ToString());
    }

    private static bool IsFrameworkConnectionLimitedHandlerName(string typeName)
    {
        return typeName.EndsWith("SocketsHttpHandler", System.StringComparison.Ordinal) ||
            typeName.EndsWith("HttpClientHandler", System.StringComparison.Ordinal);
    }

    private static bool HasMaxConnectionsPerServerInitializer(BaseObjectCreationExpressionSyntax creation)
    {
        return creation.Initializer?.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => GetAssignedMemberName(assignment.Left) == "MaxConnectionsPerServer") == true;
    }

    private static string? GetAssignedMemberName(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            _ => null
        };
    }

    private static bool UsesSemaphoreGate(
        LambdaExpressionSyntax lambda,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var semaphoreWaitReceivers = lambda.Body
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => invocation.Expression)
            .OfType<MemberAccessExpressionSyntax>()
            .Where(memberAccess => memberAccess.Name.Identifier.ValueText == "WaitAsync" &&
                IsSemaphoreSlimReceiver(memberAccess.Expression, semanticModel, cancellationToken))
            .Select(memberAccess => GetReceiverMatchKey(memberAccess.Expression, semanticModel, cancellationToken))
            .Where(receiver => receiver is not null)
            .Select(receiver => receiver!)
            .ToArray();

        if (semaphoreWaitReceivers.Length == 0)
        {
            return false;
        }

        return lambda.Body
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => invocation.Expression)
            .OfType<MemberAccessExpressionSyntax>()
            .Any(memberAccess => memberAccess.Name.Identifier.ValueText == "Release" &&
                IsSemaphoreSlimReceiver(memberAccess.Expression, semanticModel, cancellationToken) &&
                GetReceiverMatchKey(memberAccess.Expression, semanticModel, cancellationToken) is { } receiver &&
                semaphoreWaitReceivers.Contains(receiver, System.StringComparer.Ordinal));
    }

    private static string? GetReceiverMatchKey(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
        if (symbol is ILocalSymbol or IFieldSymbol or IPropertySymbol or IParameterSymbol)
        {
            var locationStart = symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceSpan.Start ?? -1;
            return symbol.Kind + ":" +
                symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ":" +
                locationStart.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return expression.ToString();
    }

    private static bool IsSemaphoreSlimReceiver(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var expressionType = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        if (expressionType is not null && expressionType is not IErrorTypeSymbol)
        {
            return IsSemaphoreSlim(expressionType);
        }

        var symbolType = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol switch
        {
            ILocalSymbol local => local.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null
        };

        if (symbolType is not null && symbolType is not IErrorTypeSymbol)
        {
            return IsSemaphoreSlim(symbolType);
        }

        return SyntacticReceiverLooksLikeSemaphoreSlim(expression);
    }

    private static bool IsSemaphoreSlim(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::System.Threading.SemaphoreSlim";
    }

    private static bool SyntacticReceiverLooksLikeSemaphoreSlim(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => LocalLooksLikeSemaphoreSlim(identifier) ||
                FieldOrPropertyLooksLikeSemaphoreSlim(identifier),
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax name } =>
                FieldOrPropertyLooksLikeSemaphoreSlim(name),
            _ => false
        };
    }

    private static bool LocalLooksLikeSemaphoreSlim(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<BlockSyntax>()?
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText &&
                variable.Parent is VariableDeclarationSyntax declaration &&
                (IsSemaphoreSlimName(declaration.Type) ||
                    variable.Initializer?.Value is BaseObjectCreationExpressionSyntax creation &&
                    IsSemaphoreSlimCreation(creation))) == true;
    }

    private static bool FieldOrPropertyLooksLikeSemaphoreSlim(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<TypeDeclarationSyntax>()?
            .Members
            .Any(member => member switch
            {
                FieldDeclarationSyntax field => IsSemaphoreSlimName(field.Declaration.Type) &&
                    field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText),
                PropertyDeclarationSyntax property => IsSemaphoreSlimName(property.Type) &&
                    property.Identifier.ValueText == identifier.Identifier.ValueText,
                _ => false
            }) == true;
    }

    private static bool IsSemaphoreSlimName(TypeSyntax type)
    {
        return type.ToString().EndsWith("SemaphoreSlim", System.StringComparison.Ordinal);
    }

    private static bool IsSemaphoreSlimCreation(BaseObjectCreationExpressionSyntax creation)
    {
        return creation is ObjectCreationExpressionSyntax objectCreation &&
            objectCreation.Type.ToString().EndsWith("SemaphoreSlim", System.StringComparison.Ordinal);
    }
}
