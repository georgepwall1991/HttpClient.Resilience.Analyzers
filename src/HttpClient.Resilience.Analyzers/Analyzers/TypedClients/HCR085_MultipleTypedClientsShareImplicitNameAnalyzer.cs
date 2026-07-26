using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.TypedClients;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR085_MultipleTypedClientsShareImplicitNameAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR085);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var registrations = context.Compilation.SyntaxTrees
            .SelectMany(tree => ServiceRegistrationCollector.CollectFrameworkRegistrations(
                tree.GetRoot(context.CancellationToken),
                GetSemanticModel(context.Compilation, tree),
                context.CancellationToken))
            .Where(registration => registration.Kind == ServiceRegistrationKind.HttpClient)
            .Select(registration => TryCreateRegistration(
                registration,
                context.Compilation,
                context.CancellationToken))
            .OfType<TypedClientRegistration>()
            .OrderBy(registration => registration.Location.SourceTree?.FilePath, System.StringComparer.Ordinal)
            .ThenBy(registration => registration.Location.SourceSpan.Start)
            .ToArray();

        var registrationsByService =
            new Dictionary<ISymbol, List<TypedClientRegistration>>(SymbolEqualityComparer.Default);

        foreach (var registration in registrations)
        {
            if (!registrationsByService.TryGetValue(registration.ServiceType, out var serviceRegistrations))
            {
                serviceRegistrations = new List<TypedClientRegistration>();
                registrationsByService.Add(registration.ServiceType, serviceRegistrations);
            }

            if (serviceRegistrations.Any(candidate => SymbolEqualityComparer.Default.Equals(
                    candidate.ImplementationType,
                    registration.ImplementationType)))
            {
                continue;
            }

            if (serviceRegistrations.FirstOrDefault() is { } firstRegistration)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.HCR085,
                    registration.Location,
                    new[] { firstRegistration.Location },
                    properties: null,
                    firstRegistration.ImplementationType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    registration.ImplementationType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    registration.ServiceType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }

            serviceRegistrations.Add(registration);
        }
    }

    private static TypedClientRegistration? TryCreateRegistration(
        ServiceRegistrationModel registration,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        if (registration.Invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name: GenericNameSyntax genericName
            } ||
            genericName.TypeArgumentList.Arguments.Count != 2)
        {
            return null;
        }

        var semanticModel = GetSemanticModel(compilation, registration.Invocation.SyntaxTree);
        if (semanticModel.GetSymbolInfo(registration.Invocation, cancellationToken).Symbol is not IMethodSymbol method ||
            method.Parameters.Any(parameter => parameter.Type.SpecialType == SpecialType.System_String))
        {
            return null;
        }

        var serviceType = semanticModel.GetTypeInfo(
            genericName.TypeArgumentList.Arguments[0],
            cancellationToken).Type as INamedTypeSymbol;
        var implementationType = semanticModel.GetTypeInfo(
            genericName.TypeArgumentList.Arguments[1],
            cancellationToken).Type as INamedTypeSymbol;

        return serviceType is null ||
            implementationType is null ||
            serviceType is IErrorTypeSymbol ||
            implementationType is IErrorTypeSymbol
            ? null
            : new TypedClientRegistration(
                serviceType,
                implementationType,
                registration.Location);
    }

#pragma warning disable RS1030 // HCR085 performs compilation-wide DI matching and needs cross-tree semantic type checks.
    private static SemanticModel GetSemanticModel(Compilation compilation, SyntaxTree syntaxTree)
    {
        return compilation.GetSemanticModel(syntaxTree);
    }
#pragma warning restore RS1030

    private sealed class TypedClientRegistration
    {
        public TypedClientRegistration(
            INamedTypeSymbol serviceType,
            INamedTypeSymbol implementationType,
            Location location)
        {
            ServiceType = serviceType;
            ImplementationType = implementationType;
            Location = location;
        }

        public INamedTypeSymbol ServiceType { get; }

        public INamedTypeSymbol ImplementationType { get; }

        public Location Location { get; }
    }
}
