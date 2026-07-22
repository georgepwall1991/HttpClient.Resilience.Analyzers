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
public sealed class HCR004_TypedClientInjectedIntoSingletonAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR004);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var roots = context.Compilation.SyntaxTrees
            .Select(tree => tree.GetRoot(context.CancellationToken))
            .ToArray();
        var registrations = roots
            .SelectMany(root => ServiceRegistrationCollector.CollectFrameworkRegistrations(
                root,
                GetSemanticModel(context.Compilation, root.SyntaxTree),
                context.CancellationToken))
            .ToArray();
        var typedClients = GetTypedClientTypeNames(
            registrations,
            context.Compilation,
            context.CancellationToken);
        if (typedClients.Count == 0)
        {
            return;
        }

        var singletonRegistrations = registrations
            .Where(registration => registration.Kind == ServiceRegistrationKind.Singleton);

        foreach (var singleton in singletonRegistrations)
        {
            if (!ConstructorConsumesTypedClient(
                    roots,
                    singleton.ImplementationTypeName ?? singleton.ServiceTypeName,
                    typedClients,
                    context.Compilation,
                    context.CancellationToken) &&
                !SingletonFactoryResolvesTypedClient(
                    singleton,
                    typedClients,
                    context.Compilation,
                    context.CancellationToken))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.HCR004,
                singleton.Location));
        }
    }

    private static ISet<string> GetTypedClientTypeNames(
        IReadOnlyCollection<ServiceRegistrationModel> registrations,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var typeNames = ServiceRegistrationCollector.GetTypedClientTypeNames(registrations);

        foreach (var registration in registrations.Where(registration => registration.Kind == ServiceRegistrationKind.HttpClient))
        {
            if (registration.Invocation.Expression is not MemberAccessExpressionSyntax
                {
                    Name: GenericNameSyntax genericName
                })
            {
                continue;
            }

            var semanticModel = GetSemanticModel(compilation, registration.Invocation.SyntaxTree);
            foreach (var typeArgument in genericName.TypeArgumentList.Arguments)
            {
                var resolvedType = semanticModel.GetTypeInfo(typeArgument, cancellationToken).Type;
                if (resolvedType is null || resolvedType is IErrorTypeSymbol)
                {
                    continue;
                }

                var qualifiedTypeName = NormalizeTypeName(resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                typeNames.Add(qualifiedTypeName);
                typeNames.Add(TypeNameUtilities.ToSimpleName(qualifiedTypeName));
            }
        }

        return typeNames;
    }

    private static bool SingletonFactoryResolvesTypedClient(
        ServiceRegistrationModel singleton,
        ISet<string> typedClients,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        return singleton.Invocation.ArgumentList.Arguments
            .Select(argument => argument.Expression)
            .Any(expression => FactoryExpressionResolvesTypedClient(
                expression,
                typedClients,
                compilation,
                cancellationToken));
    }

    private static bool FactoryExpressionResolvesTypedClient(
        ExpressionSyntax expression,
        ISet<string> typedClients,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var body = expression switch
        {
            LambdaExpressionSyntax lambda => lambda.Body,
            AnonymousMethodExpressionSyntax anonymousMethod => anonymousMethod.Block,
            _ => null
        };

        return body is not null &&
            body
                .DescendantNodesAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .Any(invocation => IsServiceProviderResolutionOfTypedClient(
                    invocation,
                    expression,
                    typedClients,
                    compilation,
                    cancellationToken));
    }

    private static bool IsServiceProviderResolutionOfTypedClient(
        InvocationExpressionSyntax invocation,
        ExpressionSyntax containingFactory,
        ISet<string> typedClients,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        return invocation.Expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax receiver,
            Name: GenericNameSyntax
            {
                Identifier.ValueText: "GetService" or "GetRequiredService",
                TypeArgumentList.Arguments.Count: 1
            } genericName
        } &&
        IsServiceProviderFactoryParameter(
            receiver,
            containingFactory,
            compilation,
            cancellationToken) &&
        IsFrameworkServiceProviderResolution(invocation, compilation, cancellationToken) &&
        TypeMatchesTypedClient(
            genericName.TypeArgumentList.Arguments[0],
            typedClients,
            compilation,
            cancellationToken);
    }

    private static bool IsFrameworkServiceProviderResolution(
        InvocationExpressionSyntax invocation,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var semanticModel = GetSemanticModel(compilation, invocation.SyntaxTree);
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return IsFrameworkServiceProviderResolution(method);
        }

        var candidateMethods = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
        return candidateMethods.Length == 0 || candidateMethods.All(IsFrameworkServiceProviderResolution);
    }

    private static bool IsFrameworkServiceProviderResolution(IMethodSymbol method)
    {
        var containingNamespace = (method.ReducedFrom ?? method).ContainingNamespace;
        return containingNamespace.IsGlobalNamespace ||
            containingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";
    }

    private static bool IsServiceProviderFactoryParameter(
        IdentifierNameSyntax receiver,
        ExpressionSyntax containingFactory,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        return containingFactory switch
        {
            SimpleLambdaExpressionSyntax simple => ParameterMatchesServiceProvider(
                simple.Parameter,
                receiver,
                compilation,
                cancellationToken),
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters
                .Any(parameter => ParameterMatchesServiceProvider(
                    parameter,
                    receiver,
                    compilation,
                    cancellationToken)),
            AnonymousMethodExpressionSyntax { ParameterList: { } parameterList } => parameterList.Parameters
                .Any(parameter => ParameterMatchesServiceProvider(
                    parameter,
                    receiver,
                    compilation,
                    cancellationToken)),
            _ => false
        };
    }

    private static bool ParameterMatchesServiceProvider(
        ParameterSyntax parameter,
        IdentifierNameSyntax receiver,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        if (parameter.Identifier.ValueText != receiver.Identifier.ValueText)
        {
            return false;
        }

        var semanticModel = GetSemanticModel(compilation, parameter.SyntaxTree);
        if (semanticModel.GetDeclaredSymbol(parameter, cancellationToken) is IParameterSymbol parameterSymbol &&
            parameterSymbol.Type is not IErrorTypeSymbol)
        {
            return IsServiceProviderType(parameterSymbol.Type);
        }

        return parameter.Type is null
            ? IsLikelyServiceProviderParameterName(parameter.Identifier.ValueText)
            : IsServiceProviderTypeName(parameter.Type);
    }

    private static bool IsLikelyServiceProviderParameterName(string name)
    {
        return name is "provider" or "serviceProvider" or "sp";
    }

    private static bool IsServiceProviderType(ITypeSymbol type)
    {
        return type.Name == "IServiceProvider" &&
            type.ContainingNamespace.ToDisplayString() == "System";
    }

    private static bool IsServiceProviderTypeName(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "IServiceProvider",
            QualifiedNameSyntax qualified => qualified.ToString() == "System.IServiceProvider" ||
                qualified.ToString() == "global::System.IServiceProvider",
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.ToString() == "global::System.IServiceProvider",
            _ => false
        };
    }

    private static bool ConstructorConsumesTypedClient(
        IEnumerable<SyntaxNode> roots,
        string singletonTypeName,
        ISet<string> typedClients,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var singletonClasses = roots
            .SelectMany(root => root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(type => DeclaredTypeMatchesRegistration(type, singletonTypeName))
            .ToArray();

        foreach (var singletonClass in singletonClasses)
        {
            foreach (var parameter in GetConstructorParameters(singletonClass))
            {
                if (parameter.Type is not null &&
                    TypeMatchesTypedClient(
                        parameter.Type,
                        typedClients,
                        compilation,
                        cancellationToken))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TypeMatchesTypedClient(
        TypeSyntax type,
        ISet<string> typedClients,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        type = UnwrapNullableType(type);

        if (TryGetTypedClientWrapperArgument(type, out var wrappedType))
        {
            return TypeMatchesTypedClient(wrappedType, typedClients, compilation, cancellationToken);
        }

        var semanticModel = GetSemanticModel(compilation, type.SyntaxTree);
        var resolvedType = semanticModel.GetTypeInfo(type, cancellationToken).Type;
        if (resolvedType is not null && resolvedType is not IErrorTypeSymbol)
        {
            return ResolvedTypeMatchesTypedClient(resolvedType, typedClients);
        }

        return TypeNameUtilities.GetComparableNames(type.ToString()).Any(typedClients.Contains);
    }

    private static bool ResolvedTypeMatchesTypedClient(ITypeSymbol resolvedType, ISet<string> typedClients)
    {
        var qualifiedTypeName = NormalizeTypeName(resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        if (typedClients.Contains(qualifiedTypeName))
        {
            return true;
        }

        var simpleName = resolvedType.Name;
        return typedClients.Contains(simpleName) &&
            (resolvedType.ContainingNamespace.IsGlobalNamespace ||
                !HasQualifiedTypedClientWithSimpleName(typedClients, simpleName));
    }

    private static bool HasQualifiedTypedClientWithSimpleName(ISet<string> typedClients, string simpleName)
    {
        return typedClients.Any(typeName =>
            NormalizeTypeName(typeName).Contains(".") &&
            TypeNameUtilities.ToSimpleName(typeName) == simpleName);
    }

#pragma warning disable RS1030 // HCR004 performs compilation-wide DI matching and needs cross-tree semantic type checks.
    private static SemanticModel GetSemanticModel(Compilation compilation, SyntaxTree syntaxTree)
    {
        return compilation.GetSemanticModel(syntaxTree);
    }
#pragma warning restore RS1030

    private static bool TryGetTypedClientWrapperArgument(TypeSyntax type, out TypeSyntax wrappedType)
    {
        switch (type)
        {
            case GenericNameSyntax genericName when IsTypedClientWrapperName(genericName.Identifier.ValueText) &&
                genericName.TypeArgumentList.Arguments.Count == 1:
                wrappedType = genericName.TypeArgumentList.Arguments[0];
                return true;
            case QualifiedNameSyntax { Right: GenericNameSyntax genericName } qualified when
                IsQualifiedTypedClientWrapperName(qualified.Left.ToString(), genericName.Identifier.ValueText) &&
                genericName.TypeArgumentList.Arguments.Count == 1:
                wrappedType = genericName.TypeArgumentList.Arguments[0];
                return true;
            case AliasQualifiedNameSyntax { Alias.Identifier.ValueText: "global", Name: GenericNameSyntax genericName } aliasQualified when
                IsQualifiedTypedClientWrapperName("global::" + aliasQualified.Name.Identifier.ValueText, genericName.Identifier.ValueText) &&
                genericName.TypeArgumentList.Arguments.Count == 1:
                wrappedType = genericName.TypeArgumentList.Arguments[0];
                return true;
            default:
                wrappedType = type;
                return false;
        }
    }

    private static bool IsTypedClientWrapperName(string typeName)
    {
        return typeName is "Func" or "Lazy" or "IEnumerable";
    }

    private static bool IsQualifiedTypedClientWrapperName(string qualifier, string typeName)
    {
        qualifier = NormalizeTypeName(qualifier);

        return typeName switch
        {
            "Func" or "Lazy" => qualifier == "System",
            "IEnumerable" => qualifier == "System.Collections.Generic",
            _ => false
        };
    }

    private static string NormalizeTypeName(string typeName)
    {
        typeName = typeName.Trim();
        return typeName.StartsWith("global::", System.StringComparison.Ordinal)
            ? typeName.Substring("global::".Length)
            : typeName;
    }

    private static TypeSyntax UnwrapNullableType(TypeSyntax type)
    {
        return type is NullableTypeSyntax nullable
            ? nullable.ElementType
            : type;
    }

    private static bool DeclaredTypeMatchesRegistration(ClassDeclarationSyntax classDeclaration, string registrationTypeName)
    {
        registrationTypeName = registrationTypeName.Trim();
        if (registrationTypeName.StartsWith("global::", System.StringComparison.Ordinal))
        {
            registrationTypeName = registrationTypeName.Substring("global::".Length);
        }

        if (registrationTypeName.Contains("."))
        {
            return GetQualifiedClassName(classDeclaration) == registrationTypeName;
        }

        return classDeclaration.Identifier.ValueText == registrationTypeName;
    }

    private static string GetQualifiedClassName(ClassDeclarationSyntax classDeclaration)
    {
        var namespaceName = string.Join(
            ".",
            classDeclaration
                .Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse()
                .Select(ns => ns.Name.ToString()));

        return string.IsNullOrEmpty(namespaceName)
            ? classDeclaration.Identifier.ValueText
            : namespaceName + "." + classDeclaration.Identifier.ValueText;
    }

    private static IEnumerable<ParameterSyntax> GetConstructorParameters(ClassDeclarationSyntax classDeclaration)
    {
        foreach (var constructor in classDeclaration.Members.OfType<ConstructorDeclarationSyntax>())
        {
            foreach (var parameter in constructor.ParameterList.Parameters)
            {
                yield return parameter;
            }
        }

        if (classDeclaration.ParameterList is null)
        {
            yield break;
        }

        foreach (var parameter in classDeclaration.ParameterList.Parameters)
        {
            yield return parameter;
        }
    }
}
