using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.KnownSymbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.Concurrency;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR080_UnboundedHttpFanOutAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] HttpCallMethodNames =
    {
        "DeleteAsync",
        "GetAsync",
        "PatchAsync",
        "PostAsync",
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
        return expression
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => IsSelectInvocationWithHttpCall(invocation, semanticModel, cancellationToken));
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

        return invocation.ArgumentList.Arguments
            .Select(argument => argument.Expression)
            .OfType<LambdaExpressionSyntax>()
            .Any(lambda => !UsesSemaphoreGate(lambda) &&
                lambda.Body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(
                    invocation => IsUnboundedHttpCall(invocation, semanticModel, cancellationToken)));
    }

    private static bool IsHttpCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            HttpCallMethodNames.Contains(memberAccess.Name.Identifier.ValueText, System.StringComparer.Ordinal) &&
            IsHttpClientReceiver(memberAccess.Expression, semanticModel, cancellationToken);
    }

    private static bool IsUnboundedHttpCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return IsHttpCall(invocation, semanticModel, cancellationToken) && !UsesConnectionLimitedClient(invocation);
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

    private static bool UsesConnectionLimitedClient(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax receiver
            })
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
            IsConnectionLimitedHttpClientCreation(value, declarations))
        {
            return true;
        }

        return ReceiverMemberUsesConnectionLimitedHandler(receiver);
    }

    private static bool ReceiverMemberUsesConnectionLimitedHandler(IdentifierNameSyntax receiver)
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
                        IsConnectionLimitedHttpClientCreation(value, typeMemberHandlers)),
                PropertyDeclarationSyntax property => HttpClientSymbols.IsHttpClientName(property.Type) &&
                    property.Identifier.ValueText == receiver.Identifier.ValueText &&
                    property.Initializer?.Value is { } value &&
                    IsConnectionLimitedHttpClientCreation(value, typeMemberHandlers),
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
        IReadOnlyCollection<VariableDeclaratorSyntax> declarations)
    {
        if (expression is not BaseObjectCreationExpressionSyntax creation ||
            !IsHttpClientCreation(creation) ||
            creation.ArgumentList is not { Arguments.Count: > 0 } argumentList)
        {
            return false;
        }

        return argumentList.Arguments
            .Select(argument => argument.Expression)
            .Any(argument => IsConnectionLimitedHandler(argument, declarations));
    }

    private static bool IsHttpClientCreation(BaseObjectCreationExpressionSyntax creation)
    {
        return creation is ImplicitObjectCreationExpressionSyntax ||
            creation is ObjectCreationExpressionSyntax objectCreation &&
            objectCreation.Type.ToString().EndsWith("HttpClient", System.StringComparison.Ordinal);
    }

    private static bool IsConnectionLimitedHandler(
        ExpressionSyntax expression,
        IReadOnlyCollection<VariableDeclaratorSyntax> declarations)
    {
        if (expression is BaseObjectCreationExpressionSyntax creation)
        {
            return IsSocketsHttpHandlerCreation(creation) &&
                HasMaxConnectionsPerServerInitializer(creation);
        }

        if (expression is not IdentifierNameSyntax identifier)
        {
            return false;
        }

        var handlerDeclaration = declarations
            .FirstOrDefault(declaration => declaration.Identifier.ValueText == identifier.Identifier.ValueText);

        return handlerDeclaration?.Initializer?.Value is BaseObjectCreationExpressionSyntax handlerCreation &&
            IsSocketsHttpHandlerCreation(handlerCreation, handlerDeclaration) &&
            HasMaxConnectionsPerServerInitializer(handlerCreation);
    }

    private static bool IsSocketsHttpHandlerCreation(
        BaseObjectCreationExpressionSyntax creation,
        VariableDeclaratorSyntax? variable = null)
    {
        return creation is ObjectCreationExpressionSyntax objectCreation &&
            objectCreation.Type.ToString().EndsWith("SocketsHttpHandler", System.StringComparison.Ordinal) ||
            creation is ImplicitObjectCreationExpressionSyntax &&
            variable?.Parent is VariableDeclarationSyntax declaration &&
            declaration.Type.ToString().EndsWith("SocketsHttpHandler", System.StringComparison.Ordinal);
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

    private static bool UsesSemaphoreGate(LambdaExpressionSyntax lambda)
    {
        var invocationNames = lambda.Body
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => invocation.Expression)
            .OfType<MemberAccessExpressionSyntax>()
            .Select(memberAccess => memberAccess.Name.Identifier.ValueText)
            .ToArray();

        return invocationNames.Contains("WaitAsync", System.StringComparer.Ordinal) &&
            invocationNames.Contains("Release", System.StringComparer.Ordinal);
    }
}
