using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.KnownSymbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.TypedClients;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR084_StringlyNamedClientAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR084);

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
        var namedRegistrations = GetNamedClientRegistrations(roots, context.Compilation, context.CancellationToken);
        if (namedRegistrations.Count == 0)
        {
            return;
        }

        foreach (var createClient in GetNamedClientUsages(roots, context.Compilation, context.CancellationToken))
        {
            if (!namedRegistrations.Contains(createClient.Name))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.HCR084,
                createClient.NameExpression.GetLocation()));
        }
    }

    private static ISet<string> GetNamedClientRegistrations(
        IEnumerable<SyntaxNode> roots,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var invocation in roots.SelectMany(root => root.DescendantNodes().OfType<InvocationExpressionSyntax>()))
        {
            var semanticModel = GetSemanticModel(compilation, invocation.SyntaxTree);
            if (TryGetNamedClientRegistration(invocation, semanticModel, cancellationToken, out var name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static IEnumerable<NamedClientUsage> GetNamedClientUsages(
        IEnumerable<SyntaxNode> roots,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        foreach (var invocation in roots.SelectMany(root => root.DescendantNodes().OfType<InvocationExpressionSyntax>()))
        {
            var semanticModel = GetSemanticModel(compilation, invocation.SyntaxTree);
            if (TryGetNamedClientUsage(invocation, semanticModel, cancellationToken, out var usage))
            {
                yield return usage;
            }
        }
    }

    private static bool TryGetNamedClientRegistration(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out string name)
    {
        name = string.Empty;
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name: IdentifierNameSyntax { Identifier.ValueText: "AddHttpClient" }
            } memberAccess ||
            invocation.ArgumentList.Arguments.Count == 0 ||
            !IsServiceCollectionReceiver(memberAccess.Expression, semanticModel, cancellationToken) ||
            !ReturnsHttpClientBuilder(invocation, semanticModel, cancellationToken) ||
            !TryGetVisibleStringLiteral(
                invocation.ArgumentList.Arguments[0].Expression,
                semanticModel,
                cancellationToken,
                out name,
                out _))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(name);
    }

    private static bool ReturnsHttpClientBuilder(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return IsHttpClientBuilderType(method.ReturnType);
        }

        var candidateMethods = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
        return candidateMethods.Length == 0 || candidateMethods.All(method => IsHttpClientBuilderType(method.ReturnType));
    }

    private static bool TryGetNamedClientUsage(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out NamedClientUsage usage)
    {
        usage = default;
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "CreateClient"
            } memberAccess ||
            invocation.ArgumentList.Arguments.Count == 0 ||
            !IsHttpClientFactoryReceiver(memberAccess.Expression, semanticModel, cancellationToken) ||
            !IsHttpClientFactoryCreateClientMethod(invocation, semanticModel, cancellationToken) ||
            !TryGetVisibleStringLiteral(
                invocation.ArgumentList.Arguments[0].Expression,
                semanticModel,
                cancellationToken,
                out var name,
                out var nameExpression) ||
            string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        usage = new NamedClientUsage(name, nameExpression);
        return true;
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

    private static bool TryGetStringLiteral(ExpressionSyntax expression, out string value)
    {
        expression = UnwrapParentheses(expression);
        if (expression is LiteralExpressionSyntax literal &&
            literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression))
        {
            value = literal.Token.ValueText;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetVisibleStringLiteral(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out string value,
        out ExpressionSyntax valueExpression)
    {
        expression = UnwrapParentheses(expression);
        valueExpression = expression;
        if (TryGetStringLiteral(expression, out value))
        {
            return true;
        }

        value = string.Empty;
        if (expression is not IdentifierNameSyntax identifier ||
            semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol is not ILocalSymbol local ||
            identifier.FirstAncestorOrSelf<BlockSyntax>() is not { } containingBlock)
        {
            return false;
        }

        var declaration = containingBlock.Statements
            .OfType<LocalDeclarationStatementSyntax>()
            .SelectMany(statement => statement.Declaration.Variables)
            .FirstOrDefault(variable => variable.SpanStart < identifier.SpanStart &&
                variable.Initializer is not null &&
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetDeclaredSymbol(variable, cancellationToken),
                    local));
        if (declaration?.Initializer is not { Value: { } initializer } ||
            containingBlock
                .DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Any(assignment => assignment.SpanStart > declaration.Span.End &&
                    assignment.SpanStart < identifier.SpanStart &&
                    SymbolEqualityComparer.Default.Equals(
                        semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol,
                        local)))
        {
            return false;
        }

        return TryGetVisibleStringLiteral(
            initializer,
            semanticModel,
            cancellationToken,
            out value,
            out valueExpression);
    }

    private static bool IsServiceCollectionReceiver(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        if (type is not null && type.TypeKind != TypeKind.Error)
        {
            return IsServiceCollectionType(type);
        }

        return semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol switch
        {
            ILocalSymbol local => IsServiceCollectionType(local.Type) || SyntacticDeclarationLooksLikeServiceCollection(local),
            IParameterSymbol parameter => IsServiceCollectionType(parameter.Type) || SyntacticDeclarationLooksLikeServiceCollection(parameter),
            IFieldSymbol field => IsServiceCollectionType(field.Type) || SyntacticDeclarationLooksLikeServiceCollection(field),
            IPropertySymbol property => IsServiceCollectionType(property.Type) || SyntacticDeclarationLooksLikeServiceCollection(property),
            _ => false
        } || SyntacticReceiverLooksLikeServiceCollection(expression);
    }

    private static bool IsHttpClientFactoryReceiver(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        if (type is not null && type.TypeKind != TypeKind.Error)
        {
            return IsHttpClientFactoryType(type);
        }

        return semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol switch
        {
            ILocalSymbol local => IsHttpClientFactoryType(local.Type) || SyntacticDeclarationLooksLikeHttpClientFactory(local),
            IParameterSymbol parameter => IsHttpClientFactoryType(parameter.Type) || SyntacticDeclarationLooksLikeHttpClientFactory(parameter),
            IFieldSymbol field => IsHttpClientFactoryType(field.Type) || SyntacticDeclarationLooksLikeHttpClientFactory(field),
            IPropertySymbol property => IsHttpClientFactoryType(property.Type) || SyntacticDeclarationLooksLikeHttpClientFactory(property),
            _ => false
        } || SyntacticReceiverLooksLikeHttpClientFactory(expression);
    }

    private static bool IsServiceCollectionType(ITypeSymbol? type)
    {
        return type is not null &&
            (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                "global::Microsoft.Extensions.DependencyInjection.IServiceCollection" ||
            type.Name == "IServiceCollection" && type.ContainingNamespace.IsGlobalNamespace);
    }

    private static bool IsHttpClientFactoryType(ITypeSymbol? type)
    {
        return type is not null &&
            (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                "global::System.Net.Http.IHttpClientFactory" ||
            type.Name == "IHttpClientFactory" && type.ContainingNamespace.IsGlobalNamespace);
    }

    private static bool IsHttpClientBuilderType(ITypeSymbol? type)
    {
        return type is not null &&
            (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                "global::Microsoft.Extensions.DependencyInjection.IHttpClientBuilder" ||
            type.Name == "IHttpClientBuilder" && type.ContainingNamespace.IsGlobalNamespace);
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
                FieldDeclarationSyntax field => IsServiceCollectionTypeName(field.Declaration.Type),
                _ => false
            });
    }

    private static bool SyntacticDeclarationLooksLikeHttpClientFactory(ISymbol symbol)
    {
        return symbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .Any(syntax => syntax switch
            {
                ParameterSyntax parameter => parameter.Type is not null &&
                    HttpClientSymbols.IsHttpClientFactoryName(parameter.Type),
                VariableDeclaratorSyntax variable => variable.Parent is VariableDeclarationSyntax declaration &&
                    HttpClientSymbols.IsHttpClientFactoryName(declaration.Type),
                PropertyDeclarationSyntax property => HttpClientSymbols.IsHttpClientFactoryName(property.Type),
                FieldDeclarationSyntax field => HttpClientSymbols.IsHttpClientFactoryName(field.Declaration.Type),
                _ => false
            });
    }

    private static bool SyntacticReceiverLooksLikeServiceCollection(ExpressionSyntax expression)
    {
        return expression is IdentifierNameSyntax identifier &&
            identifier.Identifier.ValueText is "services" or "serviceCollection" &&
            (VisibleIdentifierDeclarationType(identifier) is { } type
                ? IsServiceCollectionTypeName(type)
                : true);
    }

    private static bool SyntacticReceiverLooksLikeHttpClientFactory(ExpressionSyntax expression)
    {
        return expression is IdentifierNameSyntax identifier &&
            identifier.Identifier.ValueText is "factory" or "httpClientFactory" &&
            (VisibleIdentifierDeclarationType(identifier) is { } type
                ? HttpClientSymbols.IsHttpClientFactoryName(type)
                : true);
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

    private static ExpressionSyntax UnwrapParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

#pragma warning disable RS1030 // HCR084 performs compilation-wide named-client matching and needs cross-tree semantic type checks.
    private static SemanticModel GetSemanticModel(Compilation compilation, SyntaxTree syntaxTree)
    {
        return compilation.GetSemanticModel(syntaxTree);
    }
#pragma warning restore RS1030

    private readonly struct NamedClientUsage
    {
        public NamedClientUsage(string name, ExpressionSyntax nameExpression)
        {
            Name = name;
            NameExpression = nameExpression;
        }

        public string Name { get; }

        public ExpressionSyntax NameExpression { get; }
    }
}
