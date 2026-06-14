using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
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
        if (!IsCreateClientInvocation(assignment.Right))
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
            !IsCreateClientInvocation(initializer))
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

    private static bool IsCreateClientInvocation(ExpressionSyntax expression)
    {
        return expression is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "CreateClient"
            }
        };
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
