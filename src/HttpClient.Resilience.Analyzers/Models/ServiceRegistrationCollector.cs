using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HttpClient.Resilience.Analyzers.Models;

internal static class ServiceRegistrationCollector
{
    public static IReadOnlyList<ServiceRegistrationModel> Collect(SyntaxNode root)
    {
        return root
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(TryCreate)
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

    private static ServiceRegistrationModel? TryCreate(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name: GenericNameSyntax genericName
            } memberAccess)
        {
            return null;
        }

        var kind = TryGetKind(genericName.Identifier.ValueText);
        if (kind is null || genericName.TypeArgumentList.Arguments.Count is < 1 or > 2)
        {
            return null;
        }

        if (!IsLikelyServiceCollectionReceiver(memberAccess.Expression))
        {
            return null;
        }

        var serviceType = genericName.TypeArgumentList.Arguments[0].ToString();
        var implementationType = genericName.TypeArgumentList.Arguments.Count == 2
            ? genericName.TypeArgumentList.Arguments[1].ToString()
            : null;

        return new ServiceRegistrationModel(
            kind.Value,
            serviceType,
            implementationType,
            genericName.GetLocation(),
            invocation);
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

    private static bool IsLikelyServiceCollectionReceiver(ExpressionSyntax receiver)
    {
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
            identifier
                .FirstAncestorOrSelf<BlockSyntax>()?
                .DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .Where(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText &&
                    variable.SpanStart < identifier.SpanStart)
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
        if (memberAccess.Expression is IdentifierNameSyntax identifier &&
            VisibleIdentifierTypeName(identifier) is { } receiverTypeName &&
            FindMemberType(memberAccess, receiverTypeName) is { } receiverMemberType)
        {
            return receiverMemberType;
        }

        return FindMemberType(memberAccess, typeName: null);
    }

    private static string? VisibleIdentifierTypeName(IdentifierNameSyntax identifier)
    {
        if (VisibleIdentifierDeclarationType(identifier) is { } type &&
            type.ToString() != "var")
        {
            return TypeNameUtilities.ToSimpleName(type.ToString());
        }

        return identifier
            .FirstAncestorOrSelf<BlockSyntax>()?
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText &&
                variable.SpanStart < identifier.SpanStart)
            .Select(variable => variable.Initializer?.Value)
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => invocation.Expression)
            .OfType<MemberAccessExpressionSyntax>()
            .Select(memberAccess => TypeNameUtilities.ToSimpleName(memberAccess.Expression.ToString()))
            .FirstOrDefault();
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

    private static bool IsServiceCollectionVariableName(string name)
    {
        return name == "services" ||
            name == "serviceCollection";
    }
}
