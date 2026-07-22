using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.TypedClients;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR005_DuplicateTypedClientRegistrationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR005);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var registrations = GetCompilationRegistrations(context);
        var typedClientRegistrations = registrations
            .Where(registration => registration.Kind == ServiceRegistrationKind.HttpClient)
            .ToArray();
        if (typedClientRegistrations.Length == 0)
        {
            return;
        }

        foreach (var registration in registrations.Where(IsStandaloneServiceRegistration))
        {
            if (!MatchesAnyTypedClientRegistration(
                    registration,
                    typedClientRegistrations,
                    context.Compilation,
                    context.CancellationToken))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.HCR005,
                registration.Location));
        }
    }

    private static IReadOnlyList<ServiceRegistrationModel> GetCompilationRegistrations(CompilationAnalysisContext context)
    {
        return context.Compilation.SyntaxTrees
            .SelectMany(tree => ServiceRegistrationCollector.Collect(
                tree.GetRoot(context.CancellationToken),
                GetSemanticModel(context.Compilation, tree),
                context.CancellationToken))
            .Where(registration => IsFrameworkServiceCollectionRegistration(
                registration,
                context.Compilation,
                context.CancellationToken))
            .ToArray();
    }

    private static bool IsFrameworkServiceCollectionRegistration(
        ServiceRegistrationModel registration,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var semanticModel = GetSemanticModel(compilation, registration.Invocation.SyntaxTree);
        var symbolInfo = semanticModel.GetSymbolInfo(registration.Invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return IsFrameworkServiceCollectionRegistration(method);
        }

        var candidateMethods = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
        return candidateMethods.Length == 0 || candidateMethods.All(IsFrameworkServiceCollectionRegistration);
    }

    private static bool IsFrameworkServiceCollectionRegistration(IMethodSymbol method)
    {
        var containingNamespace = (method.ReducedFrom ?? method).ContainingNamespace;
        return containingNamespace.IsGlobalNamespace ||
            containingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";
    }

    private static bool IsStandaloneServiceRegistration(ServiceRegistrationModel registration)
    {
        return registration.Kind is
            ServiceRegistrationKind.Singleton or
            ServiceRegistrationKind.Scoped or
            ServiceRegistrationKind.Transient;
    }

    private static bool MatchesAnyTypedClientRegistration(
        ServiceRegistrationModel registration,
        IEnumerable<ServiceRegistrationModel> typedClientRegistrations,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var registrationTypes = GetRegisteredTypeNames(registration, compilation, cancellationToken).ToArray();

        return registrationTypes
            .Any(registrationTypeName => typedClientRegistrations
                .SelectMany(typedClientRegistration => GetRegisteredTypeNames(
                    typedClientRegistration,
                    compilation,
                    cancellationToken))
                .Any(typedClientTypeName => TypeNamesMatch(registrationTypeName, typedClientTypeName)));
    }

    private static IEnumerable<RegisteredTypeName> GetRegisteredTypeNames(
        ServiceRegistrationModel registration,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var resolvedTypeNames = GetResolvedRegistrationTypeNames(registration, compilation, cancellationToken)
            .ToArray();

        yield return CreateRegisteredTypeName(registration.ServiceTypeName, resolvedTypeNames);

        if (registration.ImplementationTypeName is not null)
        {
            yield return CreateRegisteredTypeName(registration.ImplementationTypeName, resolvedTypeNames);
        }
    }

    private static RegisteredTypeName CreateRegisteredTypeName(
        string typeName,
        IReadOnlyCollection<string> resolvedTypeNames)
    {
        typeName = NormalizeTypeName(typeName);
        var simpleName = TypeNameUtilities.ToSimpleName(typeName);
        var resolvedTypeName = resolvedTypeNames.FirstOrDefault(resolved =>
            resolved == typeName ||
            TypeNameUtilities.ToSimpleName(resolved) == simpleName);

        return new RegisteredTypeName(typeName, resolvedTypeName);
    }

    private static IEnumerable<string> GetResolvedRegistrationTypeNames(
        ServiceRegistrationModel registration,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var semanticModel = GetSemanticModel(compilation, registration.Invocation.SyntaxTree);

        foreach (var type in GetRegistrationTypeSyntaxes(registration.Invocation))
        {
            var resolvedType = semanticModel.GetTypeInfo(type, cancellationToken).Type;
            if (resolvedType is null || resolvedType is IErrorTypeSymbol)
            {
                continue;
            }

            yield return NormalizeTypeName(resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }
    }

    private static IEnumerable<TypeSyntax> GetRegistrationTypeSyntaxes(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax
            {
                Name: GenericNameSyntax genericName
            })
        {
            foreach (var argument in genericName.TypeArgumentList.Arguments)
            {
                yield return argument;
            }
        }

        foreach (var typeOfExpression in invocation.ArgumentList.Arguments
            .Select(argument => argument.Expression)
            .OfType<TypeOfExpressionSyntax>())
        {
            yield return typeOfExpression.Type;
        }

        foreach (var objectCreation in invocation.ArgumentList.Arguments
            .SelectMany(argument => argument.Expression.DescendantNodesAndSelf())
            .OfType<ObjectCreationExpressionSyntax>())
        {
            yield return objectCreation.Type;
        }
    }

    private static bool TypeNamesMatch(RegisteredTypeName left, RegisteredTypeName right)
    {
        if (left.ResolvedTypeName is not null && right.ResolvedTypeName is not null)
        {
            return left.ResolvedTypeName == right.ResolvedTypeName;
        }

        if (left.ResolvedTypeName is not null && IsQualifiedTypeName(right.RawTypeName))
        {
            return left.ResolvedTypeName == right.RawTypeName;
        }

        if (right.ResolvedTypeName is not null && IsQualifiedTypeName(left.RawTypeName))
        {
            return right.ResolvedTypeName == left.RawTypeName;
        }

        if (IsQualifiedTypeName(left.RawTypeName) && IsQualifiedTypeName(right.RawTypeName))
        {
            return left.RawTypeName == right.RawTypeName;
        }

        return TypeNameUtilities.ToSimpleName(left.RawTypeName) == TypeNameUtilities.ToSimpleName(right.RawTypeName);
    }

    private static string NormalizeTypeName(string typeName)
    {
        typeName = typeName.Trim();
        return typeName.StartsWith("global::", System.StringComparison.Ordinal)
            ? typeName.Substring("global::".Length)
            : typeName;
    }

    private static bool IsQualifiedTypeName(string typeName)
    {
        return typeName.Contains(".");
    }

#pragma warning disable RS1030 // HCR005 performs compilation-wide DI matching and needs cross-tree semantic type checks.
    private static SemanticModel GetSemanticModel(Compilation compilation, SyntaxTree syntaxTree)
    {
        return compilation.GetSemanticModel(syntaxTree);
    }
#pragma warning restore RS1030

    private sealed class RegisteredTypeName
    {
        public RegisteredTypeName(string rawTypeName, string? resolvedTypeName)
        {
            RawTypeName = rawTypeName;
            ResolvedTypeName = resolvedTypeName;
        }

        public string RawTypeName { get; }

        public string? ResolvedTypeName { get; }
    }
}
