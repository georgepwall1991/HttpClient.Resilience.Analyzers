using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HttpClient.Resilience.Analyzers.Models;

internal static class ServiceRegistrationCollector
{
    public static IReadOnlyList<ServiceRegistrationModel> CollectFrameworkRegistrations(
        SyntaxNode root,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return CollectCore(root, semanticModel, cancellationToken)
            .Where(registration => IsFrameworkServiceCollectionRegistration(
                registration,
                semanticModel,
                cancellationToken))
            .ToArray();
    }

    private static IReadOnlyList<ServiceRegistrationModel> CollectCore(
        SyntaxNode root,
        SemanticModel? semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return root
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => TryCreate(invocation, semanticModel, cancellationToken))
            .Where(registration => registration is not null)
            .Select(registration => registration!)
            .ToArray();
    }

    public static ISet<string> GetTypedClientTypeNames(IEnumerable<ServiceRegistrationModel> registrations)
    {
        var typeNames = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var registration in registrations.Where(registration => registration.Kind == ServiceRegistrationKind.HttpClient))
        {
            foreach (var typeName in TypeNameUtilities.GetComparableNames(registration.ServiceTypeName))
            {
                typeNames.Add(typeName);
            }

            if (registration.ImplementationTypeName is not null)
            {
                foreach (var typeName in TypeNameUtilities.GetComparableNames(registration.ImplementationTypeName))
                {
                    typeNames.Add(typeName);
                }
            }
        }

        return typeNames;
    }

    private static bool IsFrameworkServiceCollectionRegistration(
        ServiceRegistrationModel registration,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
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

    private static ServiceRegistrationModel? TryCreate(
        InvocationExpressionSyntax invocation,
        SemanticModel? semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return null;
        }

        if (!IsLikelyServiceCollectionReceiver(
                memberAccess.Expression,
                semanticModel,
                cancellationToken))
        {
            return null;
        }

        return memberAccess.Name switch
        {
            GenericNameSyntax genericName => TryCreateGenericRegistration(invocation, genericName),
            IdentifierNameSyntax identifier => TryCreateTypeofRegistration(invocation, identifier),
            _ => null
        };
    }

    private static ServiceRegistrationModel? TryCreateGenericRegistration(
        InvocationExpressionSyntax invocation,
        GenericNameSyntax genericName)
    {
        var kind = TryGetKind(genericName.Identifier.ValueText);
        if (kind is null || genericName.TypeArgumentList.Arguments.Count is < 1 or > 2)
        {
            return null;
        }

        var serviceType = genericName.TypeArgumentList.Arguments[0].ToString();
        var implementationType = genericName.TypeArgumentList.Arguments.Count == 2
            ? genericName.TypeArgumentList.Arguments[1].ToString()
            : TryGetConstructedImplementationType(invocation.ArgumentList.Arguments);

        return new ServiceRegistrationModel(
            kind.Value,
            serviceType,
            implementationType,
            genericName.GetLocation(),
            invocation);
    }

    private static ServiceRegistrationModel? TryCreateTypeofRegistration(
        InvocationExpressionSyntax invocation,
        IdentifierNameSyntax identifier)
    {
        var kind = TryGetKind(identifier.Identifier.ValueText);
        if (kind is null ||
            kind == ServiceRegistrationKind.HttpClient ||
            invocation.ArgumentList.Arguments.Count is < 1 or > 2 ||
            !TryGetTypeofArgument(invocation.ArgumentList.Arguments[0], out var serviceType))
        {
            return null;
        }

        var implementationType = invocation.ArgumentList.Arguments.Count == 2
            ? TryGetTypeofArgument(invocation.ArgumentList.Arguments[1], out var secondType)
                ? secondType
                : TryGetConstructedImplementationType(invocation.ArgumentList.Arguments[1])
            : null;

        if (invocation.ArgumentList.Arguments.Count == 2 &&
            implementationType is null &&
            !IsSupportedNonTypeofRegistrationArgument(invocation.ArgumentList.Arguments[1]))
        {
            return null;
        }

        return new ServiceRegistrationModel(
            kind.Value,
            serviceType,
            implementationType,
            identifier.GetLocation(),
            invocation);
    }

    private static bool TryGetTypeofArgument(ArgumentSyntax argument, out string typeName)
    {
        var expression = UnwrapTransparentExpressions(argument.Expression);
        if (expression is TypeOfExpressionSyntax typeOfExpression)
        {
            typeName = typeOfExpression.Type.ToString();
            return true;
        }

        typeName = string.Empty;
        return false;
    }

    private static ExpressionSyntax UnwrapTransparentExpressions(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax postfix when
                    postfix.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SuppressNullableWarningExpression):
                    expression = postfix.Operand;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private static bool IsSupportedNonTypeofRegistrationArgument(ArgumentSyntax argument)
    {
        return argument.Expression is
            LambdaExpressionSyntax or
            AnonymousMethodExpressionSyntax or
            BaseObjectCreationExpressionSyntax or
            IdentifierNameSyntax or
            MemberAccessExpressionSyntax;
    }

    private static string? TryGetConstructedImplementationType(SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        return arguments
            .Select(TryGetConstructedImplementationType)
            .FirstOrDefault(typeName => typeName is not null);
    }

    private static string? TryGetConstructedImplementationType(ArgumentSyntax argument)
    {
        return TryGetConstructedImplementationType(argument.Expression);
    }

    private static string? TryGetConstructedImplementationType(SyntaxNode node)
    {
        return node switch
        {
            ObjectCreationExpressionSyntax objectCreation => objectCreation.Type.ToString(),
            ParenthesizedExpressionSyntax parenthesized => TryGetConstructedImplementationType(parenthesized.Expression),
            LambdaExpressionSyntax lambda => TryGetConstructedImplementationType(lambda.Body),
            AnonymousMethodExpressionSyntax anonymousMethod => TryGetConstructedImplementationType(anonymousMethod.Block),
            BlockSyntax block => TryGetConstructedImplementationType(block),
            _ => null
        };
    }

    private static string? TryGetConstructedImplementationType(BlockSyntax block)
    {
        return block
            .Statements
            .OfType<ReturnStatementSyntax>()
            .Select(returnStatement => returnStatement.Expression)
            .OfType<ExpressionSyntax>()
            .Select(TryGetConstructedImplementationType)
            .FirstOrDefault(typeName => typeName is not null);
    }

    private static ServiceRegistrationKind? TryGetKind(string methodName)
    {
        return methodName switch
        {
            "AddHttpClient" => ServiceRegistrationKind.HttpClient,
            "AddSingleton" => ServiceRegistrationKind.Singleton,
            "AddScoped" => ServiceRegistrationKind.Scoped,
            "AddTransient" => ServiceRegistrationKind.Transient,
            _ => null
        };
    }

    private static bool IsLikelyServiceCollectionReceiver(
        ExpressionSyntax receiver,
        SemanticModel? semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (semanticModel?.GetTypeInfo(receiver, cancellationToken).Type is { } resolvedType &&
            resolvedType is not IErrorTypeSymbol)
        {
            return IsServiceCollectionType(resolvedType);
        }

        return receiver switch
        {
            IdentifierNameSyntax identifier => IdentifierLooksLikeServiceCollection(identifier),
            MemberAccessExpressionSyntax memberAccess => ServicesMemberLooksLikeServiceCollection(memberAccess),
            _ => false
        };
    }

    private static bool IdentifierLooksLikeServiceCollection(IdentifierNameSyntax identifier)
    {
        if (VisibleIdentifierDeclarationType(identifier) is { } type)
        {
            return IsServiceCollectionTypeName(type);
        }

        return IsServiceCollectionVariableName(identifier.Identifier.ValueText);
    }

    private static TypeSyntax? VisibleIdentifierDeclarationType(IdentifierNameSyntax identifier)
    {
        return identifier
            .Ancestors()
            .OfType<BaseMethodDeclarationSyntax>()
            .SelectMany(method => method.ParameterList.Parameters)
            .FirstOrDefault(parameter => parameter.Identifier.ValueText == identifier.Identifier.ValueText)
            ?.Type ??
            VisibleVariableDeclaratorsBefore(identifier)
                .OfType<VariableDeclaratorSyntax>()
                .Select(variable => variable.Parent)
                .OfType<VariableDeclarationSyntax>()
                .Select(declaration => declaration.Type)
                .FirstOrDefault() ??
            identifier
                .FirstAncestorOrSelf<TypeDeclarationSyntax>()?
                .Members
                .Select(member => member switch
                {
                    FieldDeclarationSyntax field when field.Declaration.Variables.Any(variable =>
                        variable.Identifier.ValueText == identifier.Identifier.ValueText) => field.Declaration.Type,
                    PropertyDeclarationSyntax property when property.Identifier.ValueText == identifier.Identifier.ValueText => property.Type,
                    _ => null
                })
                .FirstOrDefault(type => type is not null);
    }

    private static bool ServicesMemberLooksLikeServiceCollection(MemberAccessExpressionSyntax memberAccess)
    {
        return memberAccess.Name.Identifier.ValueText == "Services" &&
            ServicesMemberType(memberAccess) is { } type &&
            IsServiceCollectionTypeName(type);
    }

    private static TypeSyntax? ServicesMemberType(MemberAccessExpressionSyntax memberAccess)
    {
        if (memberAccess.Expression is not IdentifierNameSyntax identifier)
        {
            return null;
        }

        return VisibleIdentifierTypeName(identifier) is { } receiverTypeName
            ? FindMemberType(memberAccess, receiverTypeName)
            : null;
    }

    private static string? VisibleIdentifierTypeName(IdentifierNameSyntax identifier)
    {
        if (VisibleIdentifierDeclarationType(identifier) is { } type &&
            type.ToString() != "var")
        {
            return TypeNameUtilities.ToSimpleName(type.ToString());
        }

        return VisibleVariableDeclaratorsBefore(identifier)
            .Select(variable => variable.Initializer?.Value)
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => invocation.Expression)
            .OfType<MemberAccessExpressionSyntax>()
            .Select(memberAccess => TypeNameUtilities.ToSimpleName(memberAccess.Expression.ToString()))
            .FirstOrDefault();
    }

    private static IEnumerable<VariableDeclaratorSyntax> VisibleVariableDeclaratorsBefore(IdentifierNameSyntax identifier)
    {
        var scope = identifier.FirstAncestorOrSelf<BlockSyntax>() as SyntaxNode ??
            identifier.FirstAncestorOrSelf<CompilationUnitSyntax>();

        return scope?.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText &&
                variable.SpanStart < identifier.SpanStart) ??
            Enumerable.Empty<VariableDeclaratorSyntax>();
    }

    private static TypeSyntax? FindMemberType(MemberAccessExpressionSyntax memberAccess, string? typeName)
    {
        return memberAccess
            .SyntaxTree
            .GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(type => typeName is null || type.Identifier.ValueText == typeName)
            .SelectMany(type => type.Members)
            .Select(member => member switch
            {
                FieldDeclarationSyntax field when field.Declaration.Variables.Any(variable =>
                    variable.Identifier.ValueText == memberAccess.Name.Identifier.ValueText) => field.Declaration.Type,
                PropertyDeclarationSyntax property when property.Identifier.ValueText == memberAccess.Name.Identifier.ValueText => property.Type,
                _ => null
            })
            .FirstOrDefault(type => type is not null);
    }

    private static bool IsServiceCollectionTypeName(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "IServiceCollection",
            QualifiedNameSyntax qualified => qualified.ToString() == "Microsoft.Extensions.DependencyInjection.IServiceCollection" ||
                qualified.ToString() == "global::Microsoft.Extensions.DependencyInjection.IServiceCollection",
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.ToString() == "global::Microsoft.Extensions.DependencyInjection.IServiceCollection",
            _ => false
        };
    }

    private static bool IsServiceCollectionType(ITypeSymbol type)
    {
        return type.Name == "IServiceCollection" &&
            (type.ContainingNamespace.IsGlobalNamespace ||
                type.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection");
    }

    private static bool IsServiceCollectionVariableName(string name)
    {
        return name == "services" ||
            name == "serviceCollection";
    }
}
