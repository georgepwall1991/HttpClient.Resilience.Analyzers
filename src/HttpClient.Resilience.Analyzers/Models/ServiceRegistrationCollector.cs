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
            IdentifierNameSyntax identifier => IsServiceCollectionVariableName(identifier.Identifier.ValueText),
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText == "Services",
            _ => false
        };
    }

    private static bool IsServiceCollectionVariableName(string name)
    {
        return name == "services" ||
            name == "serviceCollection";
    }
}
