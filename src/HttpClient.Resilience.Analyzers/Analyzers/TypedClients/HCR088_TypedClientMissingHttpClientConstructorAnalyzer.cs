using System.Collections.Immutable;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.KnownSymbols;
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

        // A factory/configure argument supplies the instance itself — no constructor
        // requirement applies.
        if (invocation.ArgumentList.Arguments.Any(argument =>
                argument.Expression is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax))
        {
            return;
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
        var containingNamespace = (method.ReducedFrom ?? method).ContainingNamespace;
        return containingNamespace.IsGlobalNamespace ||
            containingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";
    }
}
