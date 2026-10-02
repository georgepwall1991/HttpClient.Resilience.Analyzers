using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HttpClient.Resilience.Analyzers.Models;

/// <summary>
/// Shared request-path context detection for analyzers that only fire where per-request
/// resource creation is plausible: executable member bodies, minimal-API endpoint
/// delegates, and types whose names suggest request handling. Test contexts are excluded.
/// </summary>
internal static class RequestPathContext
{
    private static readonly string[] RequestPathTypeSuffixes =
    {
        "Controller",
        "Endpoint",
        "Handler",
        "Worker",
        "Service",
        "Repository",
        "Job"
    };

    private static readonly string[] TestAttributeNames =
    {
        "Fact",
        "Theory",
        "Test",
        "TestCase",
        "TestCaseSource",
        "TestClass",
        "TestMethod",
        "DataTestMethod",
        "TestInitialize",
        "TestCleanup",
        "ClassInitialize",
        "ClassCleanup",
        "AssemblyInitialize",
        "AssemblyCleanup",
        "OneTimeSetUp",
        "OneTimeTearDown",
        "SetUp",
        "TearDown",
        "TestFixture"
    };

    private static readonly string[] MinimalApiMapMethodNames =
    {
        "Map",
        "MapDelete",
        "MapGet",
        "MapMethods",
        "MapPatch",
        "MapPost",
        "MapPut"
    };

    private static readonly string[] MinimalApiReceiverNames =
    {
        "app",
        "endpoints",
        "routeBuilder",
        "routes"
    };

    /// <summary>
    /// True when the node sits in code that executes: method, constructor, accessor,
    /// local function, lambda, expression-bodied member, or top-level statement.
    /// Field initializers and other declaration-only contexts are excluded.
    /// </summary>
    public static bool IsInExecutableCodeContext(SyntaxNode node)
    {
        return node.FirstAncestorOrSelf<MethodDeclarationSyntax>() is not null ||
            node.FirstAncestorOrSelf<ConstructorDeclarationSyntax>() is not null ||
            node.FirstAncestorOrSelf<AccessorDeclarationSyntax>() is not null ||
            node.FirstAncestorOrSelf<ArrowExpressionClauseSyntax>() is not null ||
            node.FirstAncestorOrSelf<LocalFunctionStatementSyntax>() is not null ||
            node.FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>() is not null ||
            node.FirstAncestorOrSelf<GlobalStatementSyntax>() is not null;
    }

    public static bool IsInsideLikelyRequestPathType(SyntaxNode node)
    {
        var type = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (type is null)
        {
            return false;
        }

        return RequestPathTypeSuffixes.Any(suffix =>
            type.Identifier.ValueText.EndsWith(suffix, System.StringComparison.Ordinal));
    }

    public static bool IsInsideMinimalApiEndpoint(
        SyntaxNode node,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var lambda = node.FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>();
        if (lambda?.Parent is not ArgumentSyntax argument ||
            argument.Parent?.Parent is not InvocationExpressionSyntax invocation ||
            invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            !MinimalApiMapMethodNames.Contains(memberAccess.Name.Identifier.ValueText, System.StringComparer.Ordinal))
        {
            return false;
        }

        return MinimalApiReceiverLooksLikeEndpointBuilder(
            memberAccess.Expression,
            semanticModel,
            cancellationToken);
    }

    public static bool IsInTestContext(SyntaxNode node)
    {
        var type = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (type is not null &&
            (IsTestTypeName(type.Identifier.ValueText) || HasTestAttribute(type.AttributeLists)))
        {
            return true;
        }

        return node.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>() is { } method &&
            HasTestAttribute(method.AttributeLists);
    }

    /// <summary>
    /// Best-effort declared type for an identifier: method/lambda parameter, prior local
    /// declaration, or a same-type field/property. Used by the syntactic fallbacks when
    /// semantic binding is unavailable.
    /// </summary>
    public static TypeSyntax? VisibleIdentifierDeclarationType(IdentifierNameSyntax identifier)
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
            identifier.FirstAncestorOrSelf<TypeDeclarationSyntax>()?
                .Members
                .Select(member => member switch
                {
                    FieldDeclarationSyntax field when field.Declaration.Variables
                        .Any(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText) =>
                        field.Declaration.Type,
                    PropertyDeclarationSyntax property when property.Identifier.ValueText == identifier.Identifier.ValueText =>
                        property.Type,
                    _ => null
                })
                .FirstOrDefault(type => type is not null);
    }

    private static bool MinimalApiReceiverLooksLikeEndpointBuilder(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        expression = SyntaxTransparency.Unwrap(expression);

        if (ExpressionTypeLooksLikeEndpointBuilder(expression, semanticModel, cancellationToken))
        {
            return true;
        }

        return expression switch
        {
            IdentifierNameSyntax identifier =>
                VisibleIdentifierDeclarationType(identifier) is { } type &&
                IsEndpointBuilderTypeName(type) ||
                VisibleIdentifierInitializerLooksLikeEndpointBuilder(
                    identifier,
                    semanticModel,
                    cancellationToken) ||
                MinimalApiReceiverNames.Contains(
                    identifier.Identifier.ValueText,
                    System.StringComparer.Ordinal) &&
                !VisibleIdentifierHasNonEndpointBuilderType(
                    identifier,
                    semanticModel,
                    cancellationToken),
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText is "Endpoints",
            InvocationExpressionSyntax invocation => InvocationReturnsEndpointBuilder(
                invocation,
                semanticModel,
                cancellationToken),
            _ => false
        };
    }

    private static bool ExpressionTypeLooksLikeEndpointBuilder(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return TypeSymbolLooksLikeEndpointBuilder(semanticModel.GetTypeInfo(expression, cancellationToken).Type);
    }

    private static bool TypeSymbolLooksLikeEndpointBuilder(ITypeSymbol? type)
    {
        if (type is null || type is IErrorTypeSymbol)
        {
            return false;
        }

        return type.Name is "WebApplication" or "IEndpointRouteBuilder" or "RouteGroupBuilder" ||
            type.AllInterfaces.Any(candidate => candidate.Name == "IEndpointRouteBuilder") ||
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) is
                "global::Microsoft.AspNetCore.Builder.WebApplication" or
                "global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder" or
                "global::Microsoft.AspNetCore.Routing.RouteGroupBuilder";
    }

    private static bool VisibleIdentifierHasNonEndpointBuilderType(
        IdentifierNameSyntax identifier,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var type = semanticModel.GetTypeInfo(identifier, cancellationToken).Type;
        return type is not null &&
            type is not IErrorTypeSymbol &&
            !TypeSymbolLooksLikeEndpointBuilder(type);
    }

    private static bool InvocationReturnsEndpointBuilder(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (ExpressionTypeLooksLikeEndpointBuilder(invocation, semanticModel, cancellationToken))
        {
            return true;
        }

        return invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.ValueText == "MapGroup" &&
            MinimalApiReceiverLooksLikeEndpointBuilder(
                memberAccess.Expression,
                semanticModel,
                cancellationToken);
    }

    private static bool VisibleIdentifierInitializerLooksLikeEndpointBuilder(
        IdentifierNameSyntax identifier,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var scope = identifier.FirstAncestorOrSelf<BlockSyntax>() as SyntaxNode ??
            identifier.SyntaxTree.GetRoot(cancellationToken);

        return scope
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText &&
                variable.SpanStart < identifier.SpanStart)
            .Select(variable => variable.Initializer?.Value)
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => InvocationReturnsEndpointBuilder(
                invocation,
                semanticModel,
                cancellationToken));
    }

    private static bool IsEndpointBuilderTypeName(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText is "WebApplication" or "IEndpointRouteBuilder",
            QualifiedNameSyntax qualified => qualified.ToString() is
                "Microsoft.AspNetCore.Builder.WebApplication" or
                "global::Microsoft.AspNetCore.Builder.WebApplication" or
                "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder" or
                "global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder" or
                "Microsoft.AspNetCore.Routing.RouteGroupBuilder" or
                "global::Microsoft.AspNetCore.Routing.RouteGroupBuilder",
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.ToString() is
                "global::Microsoft.AspNetCore.Builder.WebApplication" or
                "global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder" or
                "global::Microsoft.AspNetCore.Routing.RouteGroupBuilder",
            _ => false
        };
    }

    private static bool IsTestTypeName(string name)
    {
        return name.EndsWith("Test", System.StringComparison.Ordinal) ||
            name.EndsWith("Tests", System.StringComparison.Ordinal);
    }

    private static bool HasTestAttribute(SyntaxList<AttributeListSyntax> attributeLists)
    {
        return attributeLists
            .SelectMany(attributeList => attributeList.Attributes)
            .Any(attribute => IsTestAttributeName(attribute.Name));
    }

    private static bool IsTestAttributeName(NameSyntax name)
    {
        var text = name switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText,
            _ => name.ToString()
        };

        if (text.EndsWith("Attribute", System.StringComparison.Ordinal))
        {
            text = text.Substring(0, text.Length - "Attribute".Length);
        }

        return TestAttributeNames.Contains(text, System.StringComparer.Ordinal);
    }
}
