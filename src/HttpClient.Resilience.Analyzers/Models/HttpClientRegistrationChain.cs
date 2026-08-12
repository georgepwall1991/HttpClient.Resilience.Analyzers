using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HttpClient.Resilience.Analyzers.Models;

internal static class HttpClientRegistrationChain
{
    public static TypedClientRegistration? TryGetTypedClient(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return FindTypedClientImplementationInChain(invocation, semanticModel, cancellationToken) ??
            FindTypedClientImplementationForBuilderLocal(invocation, semanticModel, cancellationToken);
    }

    public static string? TryGetNamedClient(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return FindNamedClientInChain(invocation, semanticModel, cancellationToken) ??
            FindNamedClientForBuilderLocal(invocation, semanticModel, cancellationToken);
    }

    private static TypedClientRegistration? FindTypedClientImplementationInChain(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        ExpressionSyntax current = invocation;

        while (current is InvocationExpressionSyntax currentInvocation)
        {
            if (currentInvocation.Expression is MemberAccessExpressionSyntax
                {
                    Name: GenericNameSyntax
                    {
                        Identifier.ValueText: "AddHttpClient",
                        TypeArgumentList.Arguments.Count: >= 1 and <= 2
                    } genericName
                } addHttpClientAccess &&
                IsServiceCollectionReceiver(addHttpClientAccess.Expression, semanticModel, cancellationToken))
            {
                var implementationTypeIndex = genericName.TypeArgumentList.Arguments.Count == 2 ? 1 : 0;
                return CreateTypedClientRegistration(
                    genericName.TypeArgumentList.Arguments[implementationTypeIndex],
                    semanticModel,
                    cancellationToken);
            }

            if (currentInvocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                break;
            }

            current = memberAccess.Expression;
        }

        return null;
    }

    private static TypedClientRegistration? FindTypedClientImplementationForBuilderLocal(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return FindAddHttpClientInvocationForBuilderLocal(invocation) is { } addHttpClient
            ? FindTypedClientImplementationInChain(addHttpClient, semanticModel, cancellationToken)
            : null;
    }

    private static TypedClientRegistration CreateTypedClientRegistration(
        TypeSyntax implementationType,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var typeSymbol = semanticModel.GetTypeInfo(implementationType, cancellationToken).Type;
        return new TypedClientRegistration(
            implementationType.ToString(),
            typeSymbol is not null and not IErrorTypeSymbol
                ? NormalizeTypeName(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                : null);
    }

    private static string? FindNamedClientInChain(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        ExpressionSyntax current = invocation;

        while (current is InvocationExpressionSyntax currentInvocation)
        {
            if (currentInvocation.Expression is MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: "AddHttpClient"
                } addHttpClientAccess &&
                IsServiceCollectionReceiver(addHttpClientAccess.Expression, semanticModel, cancellationToken) &&
                currentInvocation.ArgumentList.Arguments.Count > 0 &&
                TryGetStringConstant(
                    currentInvocation.ArgumentList.Arguments[0].Expression,
                    semanticModel,
                    cancellationToken) is { } clientName)
            {
                return clientName;
            }

            if (currentInvocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                break;
            }

            current = memberAccess.Expression;
        }

        return null;
    }

    private static string? FindNamedClientForBuilderLocal(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return FindAddHttpClientInvocationForBuilderLocal(invocation) is { } addHttpClient
            ? FindNamedClientInChain(addHttpClient, semanticModel, cancellationToken)
            : null;
    }

    private static InvocationExpressionSyntax? FindAddHttpClientInvocationForBuilderLocal(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax builderIdentifier
            } ||
            invocation.FirstAncestorOrSelf<BlockSyntax>() is not { } block)
        {
            return null;
        }

        return block
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(variable => variable.Identifier.ValueText == builderIdentifier.Identifier.ValueText &&
                variable.SpanStart < invocation.SpanStart &&
                variable.Initializer is not null &&
                !SyntaxTransparency.LocalIsReassignedBetween(
                    block,
                    builderIdentifier.Identifier.ValueText,
                    variable.SpanStart,
                    invocation.SpanStart))
            .Select(variable => SyntaxTransparency.Unwrap(variable.Initializer!.Value))
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();
    }

    private static bool IsServiceCollectionReceiver(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return IsServiceCollectionType(semanticModel.GetTypeInfo(expression, cancellationToken).Type) ||
            semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol switch
            {
                ILocalSymbol local => IsServiceCollectionType(local.Type) || SyntacticDeclarationLooksLikeServiceCollection(local),
                IParameterSymbol parameter => IsServiceCollectionType(parameter.Type) || SyntacticDeclarationLooksLikeServiceCollection(parameter),
                IFieldSymbol field => IsServiceCollectionType(field.Type) || SyntacticDeclarationLooksLikeServiceCollection(field),
                IPropertySymbol property => IsServiceCollectionType(property.Type) || SyntacticDeclarationLooksLikeServiceCollection(property),
                _ => false
            } ||
            SyntacticReceiverLooksLikeServiceCollection(expression);
    }

    private static bool IsServiceCollectionType(ITypeSymbol? type)
    {
        return type is not null &&
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::Microsoft.Extensions.DependencyInjection.IServiceCollection";
    }

    private static bool SyntacticDeclarationLooksLikeServiceCollection(ISymbol symbol)
    {
        return symbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .Any(syntax => syntax switch
            {
                ParameterSyntax parameter => parameter.Type is not null &&
                    IsServiceCollectionTypeName(parameter.Type),
                VariableDeclaratorSyntax variable => variable.Parent is VariableDeclarationSyntax declaration &&
                    IsServiceCollectionTypeName(declaration.Type),
                PropertyDeclarationSyntax property => IsServiceCollectionTypeName(property.Type),
                _ => false
            });
    }

    private static bool SyntacticReceiverLooksLikeServiceCollection(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText is "services" or "serviceCollection" &&
                (ParameterLooksLikeServiceCollection(identifier) ||
                    LocalLooksLikeServiceCollection(identifier) ||
                    FieldOrPropertyLooksLikeServiceCollection(identifier)),
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Services" } => false,
            _ => false
        };
    }

    private static bool ParameterLooksLikeServiceCollection(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>()?
            .ParameterList.Parameters
            .Any(parameter => parameter.Identifier.ValueText == identifier.Identifier.ValueText &&
                parameter.Type is not null &&
                IsServiceCollectionTypeName(parameter.Type)) == true;
    }

    private static bool LocalLooksLikeServiceCollection(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<BlockSyntax>()?
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText &&
                variable.Parent is VariableDeclarationSyntax declaration &&
                IsServiceCollectionTypeName(declaration.Type)) == true;
    }

    private static bool FieldOrPropertyLooksLikeServiceCollection(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<TypeDeclarationSyntax>()?
            .Members
            .Any(member => member switch
            {
                FieldDeclarationSyntax field => IsServiceCollectionTypeName(field.Declaration.Type) &&
                    field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText),
                PropertyDeclarationSyntax property => IsServiceCollectionTypeName(property.Type) &&
                    property.Identifier.ValueText == identifier.Identifier.ValueText,
                _ => false
            }) == true;
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

    private static string NormalizeTypeName(string registrationTypeName)
    {
        registrationTypeName = registrationTypeName.Trim();
        if (registrationTypeName.StartsWith("global::", System.StringComparison.Ordinal))
        {
            registrationTypeName = registrationTypeName.Substring("global::".Length);
        }

        return registrationTypeName;
    }

    private static string? TryGetStringConstant(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return literal.Token.ValueText;
        }

        var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
        return constantValue.HasValue && constantValue.Value is string value
            ? value
            : null;
    }
}
