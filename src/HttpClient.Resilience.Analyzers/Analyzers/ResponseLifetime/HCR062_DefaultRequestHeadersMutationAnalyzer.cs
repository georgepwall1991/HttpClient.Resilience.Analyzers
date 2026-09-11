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
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
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
        if (FindDefaultRequestHeadersAccess(assignment.Left, context.SemanticModel, context.CancellationToken) is not { } headersAccess ||
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
        // ConfigureHttpClient delegate, or on the client injected into a registered
        // typed client's constructor, runs once per client construction — that is the
        // documented configuration pattern, not a per-request leak.
        return IsConfigureDelegateReceiver(headersAccess, semanticModel, cancellationToken) ||
            IsRegisteredTypedClientConstructorReceiver(headersAccess, semanticModel, compilation, cancellationToken);
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
