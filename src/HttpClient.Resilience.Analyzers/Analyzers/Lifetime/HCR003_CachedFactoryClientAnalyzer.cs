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
        var singletonTypes = GetKnownSingletonTypes(roots);

        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeAssignment(nodeContext, singletonTypes),
            SyntaxKind.SimpleAssignmentExpression);
        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeFieldInitializer(nodeContext, singletonTypes),
            SyntaxKind.VariableDeclarator);
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context, IReadOnlyCollection<string> singletonTypes)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        if (!IsCreateClientInvocation(assignment.Right, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        var assignedSymbol = context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol;
        if (assignedSymbol is not IFieldSymbol field)
        {
            return;
        }

        if (!IsLongLivedField(field, singletonTypes))
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

        if (!IsLongLivedField(field, singletonTypes))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR003,
            variable.Identifier.GetLocation()));
    }

    private static bool IsCreateClientInvocation(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return expression is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "CreateClient"
            } memberAccess
        } && IsHttpClientFactoryReceiver(memberAccess.Expression, semanticModel, cancellationToken);
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
            MemberAccessExpressionSyntax { Name: IdentifierNameSyntax name } => FieldOrPropertyLooksLikeHttpClientFactory(name),
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

    private static IReadOnlyCollection<string> GetKnownSingletonTypes(IEnumerable<SyntaxNode> roots)
    {
        return roots
            .SelectMany(ServiceRegistrationCollector.Collect)
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

    private static bool IsLongLivedField(IFieldSymbol field, IReadOnlyCollection<string> singletonTypes)
    {
        return field.IsStatic ||
            field.ContainingType.Name.EndsWith("Singleton", System.StringComparison.Ordinal) ||
            singletonTypes.Any(typeName => MatchesContainingType(field.ContainingType, typeName));
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
