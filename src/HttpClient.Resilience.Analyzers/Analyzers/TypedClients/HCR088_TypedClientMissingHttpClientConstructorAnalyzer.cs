using System.Collections.Immutable;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.KnownSymbols;
using HttpClient.Resilience.Analyzers.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.TypedClients;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR088_TypedClientMissingHttpClientConstructorAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR088);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name: GenericNameSyntax genericName
            } memberAccess ||
            genericName.Identifier.ValueText != "AddHttpClient" ||
            genericName.TypeArgumentList.Arguments.Count is < 1 or > 2)
        {
            return;
        }

        if (!IsFrameworkAddHttpClient(invocation, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        // Only a factory delegate supplies the instance itself. A configure lambda
        // (Action<HttpClient>) still relies on the typed client constructor to receive
        // the configured HttpClient.
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            if (HasFactoryArgument(invocation, method))
            {
                return;
            }
        }
        else
        {
            var candidates = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
            if (candidates.Length == 0)
            {
                // Unresolved: a lambda may be a factory, so stay conservative.
                if (invocation.ArgumentList.Arguments.Any(argument =>
                        argument.Expression is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax))
                {
                    return;
                }
            }
            else if (candidates.Any(candidate => HasFactoryArgument(invocation, candidate)))
            {
                return;
            }
        }

        // For AddHttpClient<TClient, TImplementation> the constructed type is the
        // second argument; for AddHttpClient<TClient> it is the only one.
        var implementationArgument = genericName.TypeArgumentList.Arguments.Count == 2
            ? genericName.TypeArgumentList.Arguments[1]
            : genericName.TypeArgumentList.Arguments[0];

        var implementationType = context.SemanticModel
            .GetTypeInfo(implementationArgument, context.CancellationToken).Type as INamedTypeSymbol;
        if (implementationType is null ||
            implementationType is IErrorTypeSymbol ||
            implementationType.IsAbstract ||
            implementationType.TypeKind != TypeKind.Class)
        {
            return;
        }

        var hasHttpClientConstructor = implementationType.InstanceConstructors
            .Any(constructor => !constructor.IsStatic &&
                constructor.Parameters.Any(parameter =>
                    HttpClientSymbols.IsHttpClient(parameter.Type)));
        if (hasHttpClientConstructor)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR088,
            implementationArgument.GetLocation(),
            implementationType.Name));
    }

    private static bool HasFactoryArgument(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        var parameters = method.Parameters;
        for (var index = 0; index < invocation.ArgumentList.Arguments.Count; index++)
        {
            var argument = invocation.ArgumentList.Arguments[index];
            IParameterSymbol? parameter;
            if (argument.NameColon is { } nameColon)
            {
                parameter = parameters.FirstOrDefault(candidate =>
                    candidate.Name == nameColon.Name.Identifier.ValueText);
            }
            else
            {
                parameter = index < parameters.Length ? parameters[index] : null;
            }

            if (parameter?.Type is INamedTypeSymbol { TypeKind: TypeKind.Delegate } delegateType &&
                delegateType.DelegateInvokeMethod is { ReturnsVoid: false })
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFrameworkAddHttpClient(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return IsDependencyInjectionExtension(method);
        }

        var candidateMethods = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
        if (candidateMethods.Length > 0)
        {
            return candidateMethods.All(IsDependencyInjectionExtension);
        }

        // Unresolved fallback: only a visibly IServiceCollection-typed receiver counts.
        return invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Expression is IdentifierNameSyntax identifier &&
            identifier.Identifier.ValueText is "services" or "serviceCollection";
    }

    private static bool IsDependencyInjectionExtension(IMethodSymbol method)
    {
        return FrameworkRegistrationMethods.IsDependencyInjectionExtension(method);
    }
}
