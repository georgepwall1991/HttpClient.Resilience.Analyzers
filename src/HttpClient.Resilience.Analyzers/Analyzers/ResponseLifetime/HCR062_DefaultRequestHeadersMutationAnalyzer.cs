using System.Collections.Immutable;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.KnownSymbols;
using HttpClient.Resilience.Analyzers.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
namespace HttpClient.Resilience.Analyzers.Analyzers.ResponseLifetime;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR062_DefaultRequestHeadersMutationAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] MutatingHeaderMethodNames =
    {
        "Add",
        "AddRange",
        "Clear",
        "ParseAdd",
        "Remove",
        "TryAddWithoutValidation"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR062);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (RequestPathContext.IsInTestContext(invocation) ||
            invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            !MutatingHeaderMethodNames.Contains(memberAccess.Name.Identifier.ValueText, System.StringComparer.Ordinal) ||
            !InvocationTargetsSystemNetHttpHeaders(invocation, context.SemanticModel, context.CancellationToken) ||
            FindDefaultRequestHeadersAccess(memberAccess.Expression, context.SemanticModel, context.CancellationToken) is not { } headersAccess ||
            IsOneTimeClientConfiguration(headersAccess, context.SemanticModel, context.Compilation, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR062,
            memberAccess.Name.GetLocation()));
    }
    private static bool InvocationTargetsSystemNetHttpHeaders(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return MethodTargetsSystemNetHttpHeaders(method);
        }

        var candidateMethods = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
        return candidateMethods.Length == 0 || candidateMethods.All(MethodTargetsSystemNetHttpHeaders);
    }

    private static bool MethodTargetsSystemNetHttpHeaders(IMethodSymbol method)
    {
        var originalMethod = method.ReducedFrom ?? method;
        return originalMethod.ContainingAssembly.Name == "System.Net.Http" &&
            originalMethod.ContainingNamespace.ToDisplayString() == "System.Net.Http.Headers";
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        if (RequestPathContext.IsInTestContext(assignment) ||
            FindDefaultRequestHeadersAccess(assignment.Left, context.SemanticModel, context.CancellationToken) is not { } headersAccess ||
            IsOneTimeClientConfiguration(headersAccess, context.SemanticModel, context.Compilation, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR062,
            assignment.Left.GetLocation()));
    }

    private static MemberAccessExpressionSyntax? FindDefaultRequestHeadersAccess(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return expression
            .DescendantNodesAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .FirstOrDefault(memberAccess => IsDefaultRequestHeadersAccess(memberAccess, semanticModel, cancellationToken));
    }

    private static bool IsOneTimeClientConfiguration(
        MemberAccessExpressionSyntax headersAccess,
        SemanticModel semanticModel,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        // Mutating DefaultRequestHeaders on the client handed to an AddHttpClient /
        // ConfigureHttpClient delegate, on the client injected into a registered
        // typed client's constructor, or on a client the containing type constructs
        // and retains in an instance member, runs once during client construction —
        // that is the documented configuration pattern, not a per-request leak.
        return IsConfigureDelegateReceiver(headersAccess, semanticModel, cancellationToken) ||
            IsRegisteredTypedClientConstructorReceiver(headersAccess, semanticModel, compilation, cancellationToken) ||
            IsOwnedClientConstructorConfiguration(headersAccess, semanticModel, cancellationToken) ||
            IsFreshClientReturnedFromMethod(headersAccess, semanticModel, cancellationToken) ||
            IsUnpublishedLocalClientConfiguration(headersAccess, semanticModel, cancellationToken);
    }

    private static bool IsUnpublishedLocalClientConfiguration(
        MemberAccessExpressionSyntax headersAccess,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        // A client created in this method and only used through member access never
        // reaches another request, so header mutation cannot leak across requests.
        if (headersAccess.Expression is not IdentifierNameSyntax identifier ||
            semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol is not ILocalSymbol local ||
            local.DeclaringSyntaxReferences.Length != 1 ||
            local.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not VariableDeclaratorSyntax variable ||
            variable.Initializer is not { } initializer ||
            !CreatesFreshHttpClient(initializer.Value, semanticModel, cancellationToken) ||
            variable.FirstAncestorOrSelf<MethodDeclarationSyntax>() is not { } method ||
            IsReassignedBefore(method, local, headersAccess.SpanStart, semanticModel, cancellationToken))
        {
            return false;
        }

        foreach (var reference in method.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (reference.SpanStart >= headersAccess.SpanStart && reference == identifier)
            {
                // The mutation receiver itself.
                continue;
            }

            if (reference.Identifier.ValueText != local.Name ||
                semanticModel.GetSymbolInfo(reference, cancellationToken).Symbol is not ILocalSymbol referencedLocal ||
                !SymbolEqualityComparer.Default.Equals(referencedLocal, local))
            {
                continue;
            }

            if (reference.Parent is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Expression == reference)
            {
                continue;
            }

            if (reference.Parent is ConditionalAccessExpressionSyntax conditionalAccess &&
                conditionalAccess.Expression == reference)
            {
                continue;
            }

            if (reference.Parent is AssignmentExpressionSyntax assignment &&
                assignment.Left == reference &&
                assignment.SpanStart > headersAccess.SpanStart)
            {
                continue;
            }

            // Passing, storing, or returning the client lets another request observe it.
            return false;
        }

        return true;
    }

    private static bool IsReassignedBefore(
        MethodDeclarationSyntax method,
        ILocalSymbol local,
        int mutationStart,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return method.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment =>
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                assignment.SpanStart < mutationStart &&
                assignment.Left is IdentifierNameSyntax left &&
                left.Identifier.ValueText == local.Name &&
                semanticModel.GetSymbolInfo(left, cancellationToken).Symbol is ILocalSymbol assignedLocal &&
                SymbolEqualityComparer.Default.Equals(assignedLocal, local));
    }

    private static bool IsFreshClientReturnedFromMethod(
        MemberAccessExpressionSyntax headersAccess,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        // A helper that creates a new client, configures it before publication, and
        // returns it has not exposed the client to concurrent requests yet.
        if (headersAccess.Expression is not IdentifierNameSyntax identifier ||
            semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol is not ILocalSymbol local ||
            local.DeclaringSyntaxReferences.Length != 1 ||
            local.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not VariableDeclaratorSyntax variable ||
            variable.Initializer is not { } initializer ||
            !CreatesFreshHttpClient(initializer.Value, semanticModel, cancellationToken) ||
            variable.FirstAncestorOrSelf<MethodDeclarationSyntax>() is not { } method)
        {
            return false;
        }

        return method.DescendantNodes()
            .OfType<ReturnStatementSyntax>()
            .Any(statement => IsDirectReturnOf(statement.Expression, local.Name, method));
    }

    private static bool IsDirectReturnOf(
        ExpressionSyntax? expression,
        string localName,
        MethodDeclarationSyntax method)
    {
        return expression is IdentifierNameSyntax identifier &&
            identifier.Identifier.ValueText == localName &&
            ReferenceEquals(identifier.FirstAncestorOrSelf<MethodDeclarationSyntax>(), method) &&
            identifier.FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>() is null &&
            identifier.FirstAncestorOrSelf<LocalFunctionStatementSyntax>() is null;
    }

    private static bool CreatesFreshHttpClient(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        expression = UnwrapTransparentExpression(expression);

        if (expression is BaseObjectCreationExpressionSyntax creation)
        {
            var createdType = semanticModel.GetTypeInfo(creation, cancellationToken).Type;
            if (createdType is not null && createdType is not IErrorTypeSymbol)
            {
                return HttpClientSymbols.IsHttpClient(createdType);
            }

            return creation is ObjectCreationExpressionSyntax objectCreation &&
                HttpClientSymbols.IsHttpClientName(objectCreation.Type);
        }

        if (expression is InvocationExpressionSyntax invocation &&
            semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method)
        {
            return IsHttpClientFactoryCreateClient(method);
        }

        return false;
    }

    private static bool IsHttpClientFactoryCreateClient(IMethodSymbol method)
    {
        // IHttpClientFactory.CreateClient always returns a new HttpClient instance. A
        // parameterless call binds to the HttpClientFactoryExtensions.CreateClient(this
        // IHttpClientFactory) convenience overload, so the receiver parameter is checked
        // as well as the interface member itself.
        var originalMethod = method.ReducedFrom ?? method;
        if (originalMethod.Name != "CreateClient")
        {
            return false;
        }

        return IsFrameworkHttpClientFactory(originalMethod.ContainingType) ||
            (originalMethod.IsExtensionMethod &&
                originalMethod.Parameters.Length == 1 &&
                IsFrameworkHttpClientFactory(originalMethod.Parameters[0].Type));
    }

    private static bool IsFrameworkHttpClientFactory(ITypeSymbol? type)
    {
        return type is { Name: "IHttpClientFactory" } &&
            type.ContainingNamespace?.ToDisplayString() == "System.Net.Http";
    }

    private static ExpressionSyntax UnwrapTransparentExpression(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case CastExpressionSyntax cast:
                    expression = cast.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax postfix
                    when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = postfix.Operand;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private static bool IsConfigureDelegateReceiver(
        MemberAccessExpressionSyntax headersAccess,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (semanticModel.GetSymbolInfo(headersAccess.Expression, cancellationToken).Symbol is not IParameterSymbol parameter ||
            parameter.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.AnonymousFunction } lambdaMethod)
        {
            return false;
        }

        var lambda = lambdaMethod.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<LambdaExpressionSyntax>()
            .FirstOrDefault();
        if (lambda?.Parent is not ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax outerInvocation } })
        {
            return false;
        }

        return IsHttpClientConfigurationMethod(outerInvocation, semanticModel, cancellationToken);
    }

    private static bool IsHttpClientConfigurationMethod(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name.Identifier.ValueText is not ("AddHttpClient" or "ConfigureHttpClient"))
        {
            return false;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return IsDependencyInjectionExtension(method);
        }

        var candidateMethods = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
        return candidateMethods.Length > 0 && candidateMethods.All(IsDependencyInjectionExtension);
    }

    private static bool IsDependencyInjectionExtension(IMethodSymbol method)
    {
        // Matches ServiceRegistrationCollector.IsFrameworkServiceCollectionRegistration:
        // global-namespace methods are accepted so source-only test stubs classify.
        var containingNamespace = (method.ReducedFrom ?? method).ContainingNamespace;
        return containingNamespace.IsGlobalNamespace ||
            containingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";
    }

    private static bool IsRegisteredTypedClientConstructorReceiver(
        MemberAccessExpressionSyntax headersAccess,
        SemanticModel semanticModel,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var receiverSymbol = semanticModel.GetSymbolInfo(headersAccess.Expression, cancellationToken).Symbol;
        var constructor = receiverSymbol switch
        {
            IParameterSymbol parameter => parameter.ContainingSymbol as IMethodSymbol,
            IFieldSymbol or IPropertySymbol => headersAccess.FirstAncestorOrSelf<ConstructorDeclarationSyntax>() is { } ctor
                ? semanticModel.GetDeclaredSymbol(ctor, cancellationToken)
                : null,
            _ => null
        };

        if (constructor is not { MethodKind: MethodKind.Constructor, ContainingType: { } containingType })
        {
            return false;
        }

        return IsRegisteredTypedClient(containingType, compilation, cancellationToken);
    }

    private static bool IsOwnedClientConstructorConfiguration(
        MemberAccessExpressionSyntax headersAccess,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (headersAccess.FirstAncestorOrSelf<ConstructorDeclarationSyntax>() is not { } constructor ||
            semanticModel.GetDeclaredSymbol(constructor, cancellationToken) is not
            {
                ContainingType: { } containingType
            } constructorSymbol)
        {
            return false;
        }

        var receiverSymbol = semanticModel.GetSymbolInfo(headersAccess.Expression, cancellationToken).Symbol;
        ISymbol? member = receiverSymbol switch
        {
            IFieldSymbol field => field,
            IPropertySymbol property => property,
            _ => null
        };

        if (member is null ||
            !SymbolEqualityComparer.Default.Equals(member.ContainingType, containingType) ||
            member.IsStatic != constructorSymbol.IsStatic)
        {
            return false;
        }

        return MemberReceivesNewHttpClient(member, constructor, semanticModel, cancellationToken);
    }

    private static bool MemberReceivesNewHttpClient(
        ISymbol member,
        ConstructorDeclarationSyntax constructor,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        foreach (var reference in member.DeclaringSyntaxReferences)
        {
            var initializer = reference.GetSyntax(cancellationToken) switch
            {
                VariableDeclaratorSyntax variable => variable.Initializer,
                PropertyDeclarationSyntax property => property.Initializer,
                _ => null
            };

            if (initializer?.Value is { } value &&
                ContainsHttpClientCreation(value, semanticModel, cancellationToken))
            {
                return true;
            }
        }

        foreach (var assignment in constructor.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                assignment.FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>() is not null ||
                assignment.FirstAncestorOrSelf<LocalFunctionStatementSyntax>() is not null ||
                !SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol,
                    member))
            {
                continue;
            }

            if (ContainsHttpClientCreation(assignment.Right, semanticModel, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsHttpClientCreation(
        SyntaxNode node,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        foreach (var creation in node.DescendantNodesAndSelf().OfType<BaseObjectCreationExpressionSyntax>())
        {
            var createdType = semanticModel.GetTypeInfo(creation, cancellationToken).Type;
            if (createdType is not null && createdType is not IErrorTypeSymbol)
            {
                if (HttpClientSymbols.IsHttpClient(createdType))
                {
                    return true;
                }

                continue;
            }

            if (creation is ObjectCreationExpressionSyntax objectCreation &&
                HttpClientSymbols.IsHttpClientName(objectCreation.Type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRegisteredTypedClient(
        INamedTypeSymbol type,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        foreach (var registration in ServiceRegistrationCollector
            .CollectFrameworkRegistrations(compilation, cancellationToken)
            .Where(registration => registration.Kind == ServiceRegistrationKind.HttpClient))
        {
            if (registration.Invocation.Expression is not MemberAccessExpressionSyntax
                {
                    Name: GenericNameSyntax genericName
                })
            {
                continue;
            }

            var registrationModel = GetSemanticModel(compilation, registration.Invocation.SyntaxTree);
            foreach (var typeArgument in genericName.TypeArgumentList.Arguments)
            {
                var resolvedType = registrationModel.GetTypeInfo(typeArgument, cancellationToken).Type;
                if (resolvedType is not null &&
                    resolvedType is not IErrorTypeSymbol &&
                    SymbolEqualityComparer.Default.Equals(resolvedType, type))
                {
                    return true;
                }
            }
        }

        return false;
    }

#pragma warning disable RS1030 // HCR062 performs compilation-wide typed-client matching and needs cross-tree semantic type checks.
    private static SemanticModel GetSemanticModel(Compilation compilation, SyntaxTree syntaxTree)
    {
        return compilation.GetSemanticModel(syntaxTree);
    }
#pragma warning restore RS1030
    private static bool IsDefaultRequestHeadersAccess(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (memberAccess.Name.Identifier.ValueText != "DefaultRequestHeaders")
        {
            return false;
        }

        var symbol = semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol;
        if (symbol is IPropertySymbol property)
        {
            return property.Name == "DefaultRequestHeaders" &&
                HttpClientSymbols.IsHttpClient(property.ContainingType);
        }

        var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
        if (receiverType is not null && receiverType is not IErrorTypeSymbol)
        {
            return HttpClientSymbols.IsHttpClient(receiverType);
        }

        var receiverSymbolType = semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol propertyReceiver => propertyReceiver.Type,
            _ => null
        };

        if (receiverSymbolType is not null && receiverSymbolType is not IErrorTypeSymbol)
        {
            return HttpClientSymbols.IsHttpClient(receiverSymbolType);
        }

        return SyntacticReceiverLooksLikeHttpClient(memberAccess.Expression);
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
