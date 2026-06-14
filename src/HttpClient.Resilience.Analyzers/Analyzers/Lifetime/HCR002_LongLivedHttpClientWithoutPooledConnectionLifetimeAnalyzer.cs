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
public sealed class HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR002);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeFieldDeclaration, SyntaxKind.FieldDeclaration);
    }

    private static void AnalyzeFieldDeclaration(SyntaxNodeAnalysisContext context)
    {
        var field = (FieldDeclarationSyntax)context.Node;

        if (!field.Modifiers.Any(SyntaxKind.StaticKeyword))
        {
            return;
        }

        foreach (var variable in field.Declaration.Variables)
        {
            if (variable.Initializer?.Value is not BaseObjectCreationExpressionSyntax creation)
            {
                continue;
            }

            if (!IsHttpClientFieldCreation(field.Declaration.Type, creation, context.SemanticModel, context.CancellationToken))
            {
                continue;
            }

            if (HasPooledConnectionLifetime(creation, context.SemanticModel, context.CancellationToken))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.HCR002,
                variable.Identifier.GetLocation()));
        }
    }

    private static bool HasPooledConnectionLifetime(
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (creation.ArgumentList is null)
        {
            return false;
        }

        foreach (var argument in creation.ArgumentList.Arguments)
        {
            if (argument.Expression is not BaseObjectCreationExpressionSyntax handlerCreation)
            {
                continue;
            }

            if (!IsSocketsHttpHandlerCreation(handlerCreation, semanticModel, cancellationToken))
            {
                continue;
            }

            if (handlerCreation.Initializer?.Expressions
                    .OfType<AssignmentExpressionSyntax>()
                    .Any(assignment => assignment.Left is IdentifierNameSyntax { Identifier.ValueText: "PooledConnectionLifetime" }) == true)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHttpClientFieldCreation(
        TypeSyntax fieldType,
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (HttpClientSymbols.IsHttpClient(semanticModel.GetTypeInfo(creation, cancellationToken).Type))
        {
            return true;
        }

        if (HttpClientSymbols.IsHttpClientName(fieldType))
        {
            return true;
        }

        return creation is ObjectCreationExpressionSyntax objectCreation &&
            HttpClientSymbols.IsHttpClientName(objectCreation.Type);
    }

    private static bool IsSocketsHttpHandlerCreation(
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (HttpClientSymbols.IsSocketsHttpHandler(semanticModel.GetTypeInfo(creation, cancellationToken).Type))
        {
            return true;
        }

        return creation is ObjectCreationExpressionSyntax objectCreation &&
            HttpClientSymbols.IsSocketsHttpHandlerName(objectCreation.Type);
    }
}
