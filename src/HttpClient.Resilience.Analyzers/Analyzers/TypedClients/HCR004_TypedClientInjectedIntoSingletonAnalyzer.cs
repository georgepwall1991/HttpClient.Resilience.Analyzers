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
            .SelectMany(ServiceRegistrationCollector.Collect)
            .ToArray();
        var typedClients = ServiceRegistrationCollector.GetTypedClientTypeNames(registrations);
        if (typedClients.Count == 0)
        {
            return;
        }

        var singletonRegistrations = registrations
            .Where(registration => registration.Kind == ServiceRegistrationKind.Singleton);

        foreach (var singleton in singletonRegistrations)
        {
            if (!ConstructorConsumesTypedClient(roots, singleton.ImplementationTypeName ?? singleton.ServiceTypeName, typedClients) &&
                !SingletonFactoryResolvesTypedClient(singleton, typedClients))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.HCR004,
                singleton.Location));
        }
    }

    private static bool SingletonFactoryResolvesTypedClient(
        ServiceRegistrationModel singleton,
        ISet<string> typedClients)
    {
        return singleton.Invocation.ArgumentList.Arguments
            .Select(argument => argument.Expression)
            .Any(expression => FactoryExpressionResolvesTypedClient(expression, typedClients));
    }

    private static bool FactoryExpressionResolvesTypedClient(
        ExpressionSyntax expression,
        ISet<string> typedClients)
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
                .Any(invocation => IsServiceProviderResolutionOfTypedClient(invocation, expression, typedClients));
    }

    private static bool IsServiceProviderResolutionOfTypedClient(
        InvocationExpressionSyntax invocation,
        ExpressionSyntax containingFactory,
        ISet<string> typedClients)
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
        IsServiceProviderFactoryParameter(receiver, containingFactory) &&
        TypeMatchesTypedClient(genericName.TypeArgumentList.Arguments[0], typedClients);
    }

    private static bool IsServiceProviderFactoryParameter(
        IdentifierNameSyntax receiver,
        ExpressionSyntax containingFactory)
    {
        return containingFactory switch
        {
            SimpleLambdaExpressionSyntax simple => ParameterMatchesServiceProvider(simple.Parameter, receiver),
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters
                .Any(parameter => ParameterMatchesServiceProvider(parameter, receiver)),
            AnonymousMethodExpressionSyntax { ParameterList: { } parameterList } => parameterList.Parameters
                .Any(parameter => ParameterMatchesServiceProvider(parameter, receiver)),
            _ => false
        };
    }

    private static bool ParameterMatchesServiceProvider(ParameterSyntax parameter, IdentifierNameSyntax receiver)
    {
        if (parameter.Identifier.ValueText != receiver.Identifier.ValueText)
        {
            return false;
        }

        return parameter.Type is null
            ? IsLikelyServiceProviderParameterName(parameter.Identifier.ValueText)
            : IsServiceProviderTypeName(parameter.Type);
    }

    private static bool IsLikelyServiceProviderParameterName(string name)
    {
        return name is "provider" or "serviceProvider" or "sp";
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

    private static bool ConstructorConsumesTypedClient(IEnumerable<SyntaxNode> roots, string singletonTypeName, ISet<string> typedClients)
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
                    TypeMatchesTypedClient(parameter.Type, typedClients))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TypeMatchesTypedClient(TypeSyntax type, ISet<string> typedClients)
    {
        type = UnwrapNullableType(type);

        if (TryGetTypedClientWrapperArgument(type, out var wrappedType))
        {
            return TypeMatchesTypedClient(wrappedType, typedClients);
        }

        return TypeNameUtilities.GetComparableNames(type.ToString()).Any(typedClients.Contains);
    }

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
