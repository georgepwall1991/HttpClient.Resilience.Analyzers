using System.Collections.Immutable;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.KnownSymbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.Lifetime;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR001_NewHttpClientInRequestPathAnalyzer : DiagnosticAnalyzer
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
        "DataTestMethod"
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
        ImmutableArray.Create(DiagnosticDescriptors.HCR001);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression, SyntaxKind.ImplicitObjectCreationExpression);
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (BaseObjectCreationExpressionSyntax)context.Node;
        if (!IsHttpClientCreation(creation, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        if (!IsInExecutableCodeContext(creation))
        {
            return;
        }

        if (IsInTestContext(creation))
        {
            return;
        }

        if (!HasHighConfidenceRequestPathEvidence(creation))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR001,
            creation.GetLocation()));
    }

    private static bool HasHighConfidenceRequestPathEvidence(SyntaxNode node)
    {
        return IsInsideLoop(node) ||
            IsDisposedInUsing(node) ||
            IsInsideMinimalApiEndpoint(node) ||
            IsInsideLikelyRequestPathType(node);
    }

    private static bool IsInExecutableCodeContext(SyntaxNode node)
    {
        return node.FirstAncestorOrSelf<MethodDeclarationSyntax>() is not null ||
            node.FirstAncestorOrSelf<LocalFunctionStatementSyntax>() is not null ||
            node.FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>() is not null ||
            node.FirstAncestorOrSelf<GlobalStatementSyntax>() is not null;
    }

    private static bool IsHttpClientCreation(
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var createdType = semanticModel.GetTypeInfo(creation, cancellationToken).Type;
        if (createdType is not null && createdType is not IErrorTypeSymbol)
        {
            return HttpClientSymbols.IsHttpClient(createdType);
        }

        return creation is ObjectCreationExpressionSyntax objectCreation &&
            HttpClientSymbols.IsHttpClientName(objectCreation.Type);
    }

    private static bool IsInsideLoop(SyntaxNode node)
    {
        return node.FirstAncestorOrSelf<ForStatementSyntax>() is not null ||
            node.FirstAncestorOrSelf<ForEachStatementSyntax>() is not null ||
            node.FirstAncestorOrSelf<WhileStatementSyntax>() is not null ||
            node.FirstAncestorOrSelf<DoStatementSyntax>() is not null;
    }

    private static bool IsDisposedInUsing(SyntaxNode node)
    {
        return node.FirstAncestorOrSelf<UsingStatementSyntax>() is not null ||
            node.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>()?.UsingKeyword.IsKind(SyntaxKind.UsingKeyword) == true;
    }

    private static bool IsInsideMinimalApiEndpoint(SyntaxNode node)
    {
        var lambda = node.FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>();
        if (lambda?.Parent is not ArgumentSyntax argument ||
            argument.Parent?.Parent is not InvocationExpressionSyntax invocation ||
            invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            !MinimalApiMapMethodNames.Contains(memberAccess.Name.Identifier.ValueText, System.StringComparer.Ordinal))
        {
            return false;
        }

        return MinimalApiReceiverLooksLikeEndpointBuilder(memberAccess.Expression);
    }

    private static bool MinimalApiReceiverLooksLikeEndpointBuilder(ExpressionSyntax expression)
    {
        expression = UnwrapParentheses(expression);

        return expression switch
        {
            IdentifierNameSyntax identifier => MinimalApiReceiverNames.Contains(
                    identifier.Identifier.ValueText,
                    System.StringComparer.Ordinal) ||
                VisibleIdentifierDeclarationType(identifier) is { } type &&
                IsEndpointBuilderTypeName(type) ||
                VisibleIdentifierInitializerLooksLikeEndpointBuilder(identifier),
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText is "Endpoints",
            InvocationExpressionSyntax invocation => InvocationReturnsEndpointBuilder(invocation),
            _ => false
        };
    }

    private static bool InvocationReturnsEndpointBuilder(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.ValueText == "MapGroup" &&
            MinimalApiReceiverLooksLikeEndpointBuilder(memberAccess.Expression);
    }

    private static bool VisibleIdentifierInitializerLooksLikeEndpointBuilder(IdentifierNameSyntax identifier)
    {
        var scope = identifier.FirstAncestorOrSelf<BlockSyntax>() as SyntaxNode ??
            identifier.SyntaxTree.GetRoot();

        return scope
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText &&
                variable.SpanStart < identifier.SpanStart)
            .Select(variable => variable.Initializer?.Value)
            .OfType<InvocationExpressionSyntax>()
            .Any(InvocationReturnsEndpointBuilder);
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
                .FirstOrDefault();
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
                "global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder",
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.ToString() is
                "global::Microsoft.AspNetCore.Builder.WebApplication" or
                "global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder",
            _ => false
        };
    }

    private static ExpressionSyntax UnwrapParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static bool IsInsideLikelyRequestPathType(SyntaxNode node)
    {
        var type = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (type is null)
        {
            return false;
        }

        foreach (var suffix in RequestPathTypeSuffixes)
        {
            if (type.Identifier.ValueText.EndsWith(suffix, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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
}
