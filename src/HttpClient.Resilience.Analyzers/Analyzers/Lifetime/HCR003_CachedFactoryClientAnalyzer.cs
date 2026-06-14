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

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context, ISet<string> singletonTypes)
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

    private static void AnalyzeFieldInitializer(SyntaxNodeAnalysisContext context, ISet<string> singletonTypes)
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
        if (HttpClientSymbols.IsHttpClientFactory(semanticModel.GetTypeInfo(expression, cancellationToken).Type))
        {
            return true;
        }

        return semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol switch
        {
            ILocalSymbol local => HttpClientSymbols.IsHttpClientFactory(local.Type),
            IParameterSymbol parameter => HttpClientSymbols.IsHttpClientFactory(parameter.Type),
            IFieldSymbol field => HttpClientSymbols.IsHttpClientFactory(field.Type),
            IPropertySymbol property => HttpClientSymbols.IsHttpClientFactory(property.Type),
            _ => false
        } || SyntacticReceiverLooksLikeHttpClientFactory(expression);
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
                HttpClientSymbols.IsHttpClientFactoryName(parameter.Type)) == true;
    }

    private static bool LocalLooksLikeHttpClientFactory(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<BlockSyntax>()?
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText &&
                variable.Parent is VariableDeclarationSyntax declaration &&
                HttpClientSymbols.IsHttpClientFactoryName(declaration.Type)) == true;
    }

    private static bool FieldOrPropertyLooksLikeHttpClientFactory(IdentifierNameSyntax identifier)
    {
        var root = identifier.SyntaxTree.GetRoot();

        return root
            .DescendantNodes()
            .Any(node => node switch
            {
                FieldDeclarationSyntax field => HttpClientSymbols.IsHttpClientFactoryName(field.Declaration.Type) &&
                    field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText),
                PropertyDeclarationSyntax property => HttpClientSymbols.IsHttpClientFactoryName(property.Type) &&
                    property.Identifier.ValueText == identifier.Identifier.ValueText,
                _ => false
            });
    }

    private static HashSet<string> GetKnownSingletonTypes(IEnumerable<SyntaxNode> roots)
    {
        return new HashSet<string>(
            roots
                .SelectMany(ServiceRegistrationCollector.Collect)
                .Where(registration => registration.Kind == ServiceRegistrationKind.Singleton)
                .SelectMany(registration => new[]
                {
                    registration.ServiceTypeName,
                    registration.ImplementationTypeName
                })
                .Where(typeName => typeName is not null)
                .SelectMany(typeName => TypeNameUtilities.GetComparableNames(typeName!)),
            System.StringComparer.Ordinal);
    }

    private static bool IsLongLivedField(IFieldSymbol field, ISet<string> singletonTypes)
    {
        return field.IsStatic ||
            field.ContainingType.Name.EndsWith("Singleton", System.StringComparison.Ordinal) ||
            singletonTypes.Contains(field.ContainingType.Name);
    }
}
