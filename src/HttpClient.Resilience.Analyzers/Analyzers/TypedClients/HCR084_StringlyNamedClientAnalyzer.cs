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
            !TryGetStringLiteral(invocation.ArgumentList.Arguments[0].Expression, out name))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(name);
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
            !TryGetStringLiteral(invocation.ArgumentList.Arguments[0].Expression, out var name) ||
            string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        usage = new NamedClientUsage(name, invocation.ArgumentList.Arguments[0].Expression);
        return true;
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

    private static bool IsServiceCollectionReceiver(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        if (IsServiceCollectionType(type))
        {
            return true;
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
        if (HttpClientSymbols.IsHttpClientFactory(type))
        {
            return true;
        }

        return semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol switch
        {
            ILocalSymbol local => HttpClientSymbols.IsHttpClientFactory(local.Type) || SyntacticDeclarationLooksLikeHttpClientFactory(local),
            IParameterSymbol parameter => HttpClientSymbols.IsHttpClientFactory(parameter.Type) || SyntacticDeclarationLooksLikeHttpClientFactory(parameter),
            IFieldSymbol field => HttpClientSymbols.IsHttpClientFactory(field.Type) || SyntacticDeclarationLooksLikeHttpClientFactory(field),
            IPropertySymbol property => HttpClientSymbols.IsHttpClientFactory(property.Type) || SyntacticDeclarationLooksLikeHttpClientFactory(property),
            _ => false
        } || SyntacticReceiverLooksLikeHttpClientFactory(expression);
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
