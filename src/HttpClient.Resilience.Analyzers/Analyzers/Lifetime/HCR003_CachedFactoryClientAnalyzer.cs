using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.KnownSymbols;
using HttpClient.Resilience.Analyzers.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.Lifetime;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR003_CachedFactoryClientAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR003);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationStartAnalysisContext context)
    {
        var roots = context.Compilation.SyntaxTrees
            .Select(tree => tree.GetRoot(context.CancellationToken))
            .ToArray();
        var singletonTypes = GetKnownSingletonTypes(
            roots,
            context.Compilation,
            context.CancellationToken);

        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeAssignment(nodeContext, singletonTypes),
            SyntaxKind.SimpleAssignmentExpression);
        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeFieldInitializer(nodeContext, singletonTypes),
            SyntaxKind.VariableDeclarator);
        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzePropertyInitializer(nodeContext, singletonTypes),
            SyntaxKind.PropertyDeclaration);
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context, IReadOnlyCollection<string> singletonTypes)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        if (!ExpressionCreatesFactoryClient(assignment.Right, assignment, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        var assignedSymbol = context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol;
        if (!IsLongLivedHttpClientMember(assignedSymbol, singletonTypes))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR003,
            assignment.Left.GetLocation()));
    }

    private static void AnalyzeFieldInitializer(SyntaxNodeAnalysisContext context, IReadOnlyCollection<string> singletonTypes)
    {
        var variable = (VariableDeclaratorSyntax)context.Node;
        if (variable.Parent?.Parent is not FieldDeclarationSyntax ||
            variable.Initializer is not { Value: { } initializer } ||
            !IsCreateClientInvocation(initializer, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        if (context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken) is not IFieldSymbol field)
        {
            return;
        }

        if (!IsLongLivedField(field, singletonTypes) ||
            !IsHttpClientField(field))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR003,
            variable.Identifier.GetLocation()));
    }

    private static void AnalyzePropertyInitializer(SyntaxNodeAnalysisContext context, IReadOnlyCollection<string> singletonTypes)
    {
        var property = (PropertyDeclarationSyntax)context.Node;
        if (property.Initializer is not { Value: { } initializer } ||
            !IsCreateClientInvocation(initializer, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        if (context.SemanticModel.GetDeclaredSymbol(property, context.CancellationToken) is not IPropertySymbol propertySymbol)
        {
            return;
        }

        if (!IsLongLivedProperty(propertySymbol, singletonTypes) ||
            !IsHttpClientProperty(propertySymbol))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR003,
            property.Identifier.GetLocation()));
    }

    private static bool ExpressionCreatesFactoryClient(
        ExpressionSyntax expression,
        SyntaxNode context,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        expression = UnwrapParentheses(expression);

        return IsCreateClientInvocation(expression, semanticModel, cancellationToken) ||
            expression is IdentifierNameSyntax identifier &&
            LocalVariableCreatesFactoryClient(identifier, context, semanticModel, cancellationToken);
    }

    private static bool LocalVariableCreatesFactoryClient(
        IdentifierNameSyntax identifier,
        SyntaxNode context,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var containingBlock = context.FirstAncestorOrSelf<BlockSyntax>();
        if (containingBlock is null)
        {
            return false;
        }

        var referencedSymbol = semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
        if (referencedSymbol is not ILocalSymbol)
        {
            return false;
        }

        return containingBlock
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(variable => SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetDeclaredSymbol(variable, cancellationToken),
                    referencedSymbol) &&
                variable.SpanStart < context.SpanStart &&
                variable.Initializer is not null &&
                !LocalIsReassignedBetweenDeclarationAndUse(
                    containingBlock,
                    variable,
                    context,
                    semanticModel,
                    cancellationToken))
            .Any(variable => IsCreateClientInvocation(
                variable.Initializer!.Value,
                semanticModel,
                cancellationToken));
    }

    private static bool LocalIsReassignedBetweenDeclarationAndUse(
        BlockSyntax containingBlock,
        VariableDeclaratorSyntax variable,
        SyntaxNode context,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var localSymbol = semanticModel.GetDeclaredSymbol(variable, cancellationToken);

        return containingBlock
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.SpanStart > variable.SpanStart &&
                assignment.SpanStart < context.SpanStart &&
                assignment.Left is IdentifierNameSyntax identifier &&
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol,
                    localSymbol));
    }

    private static bool IsCreateClientInvocation(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        expression = UnwrapParentheses(expression);

        return expression is InvocationExpressionSyntax invocation &&
            invocation is
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: "CreateClient"
                } memberAccess
            } &&
            IsHttpClientFactoryReceiver(memberAccess.Expression, semanticModel, cancellationToken) &&
            IsHttpClientFactoryCreateClientMethod(invocation, semanticModel, cancellationToken);
    }

    private static bool IsHttpClientFactoryCreateClientMethod(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return IsHttpClientFactoryMethod(method);
        }

        var candidateMethods = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
        return candidateMethods.Length == 0 || candidateMethods.All(IsHttpClientFactoryMethod);
    }

    private static bool IsHttpClientFactoryMethod(IMethodSymbol method)
    {
        return IsHttpClientFactoryType((method.ReducedFrom ?? method).ContainingType);
    }

    private static bool IsHttpClientFactoryReceiver(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var expressionType = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        if (expressionType is not null && expressionType is not IErrorTypeSymbol)
        {
            return IsHttpClientFactoryType(expressionType);
        }

        var symbolType = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null
        };

        if (symbolType is not null && symbolType is not IErrorTypeSymbol)
        {
            return IsHttpClientFactoryType(symbolType);
        }

        return SyntacticReceiverLooksLikeHttpClientFactory(expression);
    }

    private static bool IsHttpClientFactoryType(ITypeSymbol? type)
    {
        return type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) is
            "global::System.Net.Http.IHttpClientFactory" or
            "global::IHttpClientFactory";
    }

    private static bool SyntacticReceiverLooksLikeHttpClientFactory(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => ParameterLooksLikeHttpClientFactory(identifier) ||
                LocalLooksLikeHttpClientFactory(identifier) ||
                FieldOrPropertyLooksLikeHttpClientFactory(identifier),
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax name } =>
                FieldOrPropertyLooksLikeHttpClientFactory(name),
            _ => false
        };
    }

    private static bool ParameterLooksLikeHttpClientFactory(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>()?
            .ParameterList.Parameters
            .Any(parameter => parameter.Identifier.ValueText == identifier.Identifier.ValueText &&
                parameter.Type is not null &&
                IsHttpClientFactoryTypeName(parameter.Type)) == true;
    }

    private static bool LocalLooksLikeHttpClientFactory(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<BlockSyntax>()?
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText &&
                variable.Parent is VariableDeclarationSyntax declaration &&
                IsHttpClientFactoryTypeName(declaration.Type)) == true;
    }

    private static bool FieldOrPropertyLooksLikeHttpClientFactory(IdentifierNameSyntax identifier)
    {
        var root = identifier.SyntaxTree.GetRoot();

        return root
            .DescendantNodes()
            .Any(node => node switch
            {
                FieldDeclarationSyntax field => IsHttpClientFactoryTypeName(field.Declaration.Type) &&
                    field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText),
                PropertyDeclarationSyntax property => IsHttpClientFactoryTypeName(property.Type) &&
                    property.Identifier.ValueText == identifier.Identifier.ValueText,
                _ => false
            });
    }

    private static bool IsHttpClientFactoryTypeName(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "IHttpClientFactory",
            QualifiedNameSyntax qualified => qualified.ToString() == "System.Net.Http.IHttpClientFactory" ||
                qualified.ToString() == "global::System.Net.Http.IHttpClientFactory",
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.ToString() == "global::System.Net.Http.IHttpClientFactory",
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

    private static IReadOnlyCollection<string> GetKnownSingletonTypes(
        IEnumerable<SyntaxNode> roots,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        return roots
            .SelectMany(root => ServiceRegistrationCollector.Collect(
                root,
                GetSemanticModel(compilation, root.SyntaxTree),
                cancellationToken))
            .Where(registration => registration.Kind == ServiceRegistrationKind.Singleton)
            .SelectMany(registration => new[]
            {
                registration.ServiceTypeName,
                registration.ImplementationTypeName
            })
            .Where(typeName => typeName is not null)
            .Select(typeName => NormalizeTypeName(typeName!))
            .ToArray();
    }

#pragma warning disable RS1030 // HCR003 performs compilation-wide singleton matching and needs cross-tree semantic checks.
    private static SemanticModel GetSemanticModel(Compilation compilation, SyntaxTree syntaxTree)
    {
        return compilation.GetSemanticModel(syntaxTree);
    }
#pragma warning restore RS1030

    private static bool IsLongLivedField(IFieldSymbol field, IReadOnlyCollection<string> singletonTypes)
    {
        return field.IsStatic ||
            field.ContainingType.Name.EndsWith("Singleton", System.StringComparison.Ordinal) ||
            singletonTypes.Any(typeName => MatchesContainingType(field.ContainingType, typeName));
    }

    private static bool IsLongLivedProperty(IPropertySymbol property, IReadOnlyCollection<string> singletonTypes)
    {
        return property.IsStatic ||
            property.ContainingType.Name.EndsWith("Singleton", System.StringComparison.Ordinal) ||
            singletonTypes.Any(typeName => MatchesContainingType(property.ContainingType, typeName));
    }

    private static bool IsLongLivedHttpClientMember(ISymbol? symbol, IReadOnlyCollection<string> singletonTypes)
    {
        return symbol switch
        {
            IFieldSymbol field => IsLongLivedField(field, singletonTypes) &&
                IsHttpClientField(field),
            IPropertySymbol property => IsLongLivedProperty(property, singletonTypes) &&
                IsHttpClientProperty(property),
            _ => false
        };
    }

    private static bool IsHttpClientField(IFieldSymbol field)
    {
        return HttpClientSymbols.IsHttpClient(field.Type) ||
            field.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax())
                .OfType<VariableDeclaratorSyntax>()
                .Any(variable => variable.Parent is VariableDeclarationSyntax declaration &&
                    HttpClientSymbols.IsHttpClientName(declaration.Type));
    }

    private static bool IsHttpClientProperty(IPropertySymbol property)
    {
        return HttpClientSymbols.IsHttpClient(property.Type) ||
            property.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax())
                .OfType<PropertyDeclarationSyntax>()
                .Any(propertyDeclaration => HttpClientSymbols.IsHttpClientName(propertyDeclaration.Type));
    }

    private static bool MatchesContainingType(INamedTypeSymbol containingType, string registrationTypeName)
    {
        if (registrationTypeName.Contains("."))
        {
            return NormalizeTypeName(containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)) == registrationTypeName;
        }

        return containingType.Name == TypeNameUtilities.ToSimpleName(registrationTypeName);
    }

    private static string NormalizeTypeName(string typeName)
    {
        typeName = typeName.Trim();
        return typeName.StartsWith("global::", System.StringComparison.Ordinal)
            ? typeName.Substring("global::".Length)
            : typeName;
    }
}
