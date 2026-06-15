using System.Collections.Immutable;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.Resilience;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR082_PerRequestResiliencePipelineAnalyzer : DiagnosticAnalyzer
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

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR082);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name.Identifier.ValueText != "Build" ||
            !ReceiverLooksLikeResiliencePipelineBuilder(
                memberAccess.Expression,
                context.SemanticModel,
                context.CancellationToken) ||
            !IsInExecutableCodeContext(invocation) ||
            IsInTestContext(invocation) ||
            !HasHighConfidenceRequestPathEvidence(invocation, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR082,
            memberAccess.Name.GetLocation()));
    }

    private static bool ReceiverLooksLikeResiliencePipelineBuilder(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        expression = UnwrapParentheses(expression);

        var expressionType = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        if (expressionType is not null && expressionType is not IErrorTypeSymbol)
        {
            return IsResiliencePipelineBuilderType(expressionType);
        }

        return expression switch
        {
            ObjectCreationExpressionSyntax objectCreation => IsResiliencePipelineBuilderCreation(
                objectCreation,
                semanticModel,
                cancellationToken),
            ImplicitObjectCreationExpressionSyntax implicitObjectCreation => IsResiliencePipelineBuilderCreation(
                implicitObjectCreation,
                semanticModel,
                cancellationToken),
            IdentifierNameSyntax identifier => VisibleIdentifierLooksLikeResiliencePipelineBuilder(
                identifier,
                semanticModel,
                cancellationToken),
            MemberAccessExpressionSyntax memberAccess => ReceiverLooksLikeResiliencePipelineBuilder(
                memberAccess.Expression,
                semanticModel,
                cancellationToken),
            InvocationExpressionSyntax invocation => invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                ReceiverLooksLikeResiliencePipelineBuilder(
                    memberAccess.Expression,
                    semanticModel,
                    cancellationToken),
            _ => false
        };
    }

    private static bool IsResiliencePipelineBuilderCreation(
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var createdType = semanticModel.GetTypeInfo(creation, cancellationToken).Type;
        if (createdType is not null && createdType is not IErrorTypeSymbol)
        {
            return IsResiliencePipelineBuilderType(createdType);
        }

        return creation is ObjectCreationExpressionSyntax objectCreation &&
            IsResiliencePipelineBuilderTypeName(objectCreation.Type);
    }

    private static bool VisibleIdentifierLooksLikeResiliencePipelineBuilder(
        IdentifierNameSyntax identifier,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolType = semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null
        };

        if (symbolType is not null && symbolType is not IErrorTypeSymbol)
        {
            return IsResiliencePipelineBuilderType(symbolType);
        }

        return VisibleIdentifierDeclarationType(identifier) is { } type &&
            IsResiliencePipelineBuilderTypeName(type) ||
            VisibleIdentifierInitializerLooksLikeResiliencePipelineBuilder(
                identifier,
                semanticModel,
                cancellationToken);
    }

    private static bool VisibleIdentifierInitializerLooksLikeResiliencePipelineBuilder(
        IdentifierNameSyntax identifier,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return identifier.FirstAncestorOrSelf<BlockSyntax>()?
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText &&
                variable.SpanStart < identifier.SpanStart &&
                variable.Initializer is not null &&
                !LocalIsReassignedBetween(
                    variable.FirstAncestorOrSelf<BlockSyntax>()!,
                    identifier.Identifier.ValueText,
                    variable.SpanStart,
                    identifier.SpanStart))
            .Select(variable => variable.Initializer!.Value)
            .Any(initializer => ReceiverLooksLikeResiliencePipelineBuilder(
                initializer,
                semanticModel,
                cancellationToken)) == true;
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

    private static bool IsResiliencePipelineBuilderType(ITypeSymbol type)
    {
        return type is INamedTypeSymbol namedType &&
            namedType.Name == "ResiliencePipelineBuilder" &&
            namedType.ContainingNamespace.ToDisplayString() == "Polly";
    }

    private static bool IsResiliencePipelineBuilderTypeName(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "ResiliencePipelineBuilder",
            GenericNameSyntax generic => generic.Identifier.ValueText == "ResiliencePipelineBuilder",
            QualifiedNameSyntax qualified => qualified.ToString() is
                "Polly.ResiliencePipelineBuilder" or
                "global::Polly.ResiliencePipelineBuilder" ||
                qualified.Right is GenericNameSyntax { Identifier.ValueText: "ResiliencePipelineBuilder" } &&
                qualified.Left.ToString() is "Polly" or "global::Polly",
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.ToString().StartsWith(
                "global::Polly.ResiliencePipelineBuilder",
                System.StringComparison.Ordinal),
            _ => false
        };
    }

    private static bool IsInExecutableCodeContext(SyntaxNode node)
    {
        return node.FirstAncestorOrSelf<MethodDeclarationSyntax>() is not null ||
            node.FirstAncestorOrSelf<LocalFunctionStatementSyntax>() is not null ||
            node.FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>() is not null ||
            node.FirstAncestorOrSelf<GlobalStatementSyntax>() is not null;
    }

    private static bool HasHighConfidenceRequestPathEvidence(
        SyntaxNode node,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return IsInsideMinimalApiEndpoint(node, semanticModel, cancellationToken) ||
            IsInsideLikelyRequestPathType(node);
    }

    private static bool IsInsideMinimalApiEndpoint(
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

    private static bool MinimalApiReceiverLooksLikeEndpointBuilder(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        expression = UnwrapParentheses(expression);

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

    private static bool IsInsideLikelyRequestPathType(SyntaxNode node)
    {
        var type = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (type is null)
        {
            return false;
        }

        return RequestPathTypeSuffixes.Any(suffix =>
            type.Identifier.ValueText.EndsWith(suffix, System.StringComparison.Ordinal));
    }

    private static bool IsInTestContext(SyntaxNode node)
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

    private static bool LocalIsReassignedBetween(
        BlockSyntax containingBlock,
        string localName,
        int start,
        int end)
    {
        return containingBlock
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.SpanStart > start &&
                assignment.SpanStart < end &&
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                assignment.Left is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == localName);
    }

    private static ExpressionSyntax UnwrapParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }
}
