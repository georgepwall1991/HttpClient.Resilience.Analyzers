using System;
using System.Collections.Immutable;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.KnownSymbols;
using HttpClient.Resilience.Analyzers.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.Lifetime;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR087_BaseAddressMissingTrailingSlashAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR087);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        if (assignment.Left is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "BaseAddress"
            } memberAccess)
        {
            return;
        }

        if (!IsHttpClientReceiver(memberAccess.Expression, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        var constantValue = context.SemanticModel
            .GetConstantValue(assignment.Right, context.CancellationToken);
        if (!constantValue.HasValue || constantValue.Value is not string uriText)
        {
            // new Uri("literal") — extract the literal argument.
            if (assignment.Right is ObjectCreationExpressionSyntax creation &&
                creation.ArgumentList is { Arguments.Count: 1 } argumentList &&
                argumentList.Arguments[0].Expression is LiteralExpressionSyntax literal &&
                context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type is
                { Name: "Uri", ContainingNamespace.Name: "System" } &&
                context.SemanticModel.GetConstantValue(literal, context.CancellationToken) is
                { HasValue: true, Value: string literalText })
            {
                uriText = literalText;
            }
            else
            {
                return;
            }
        }

        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri) ||
            string.IsNullOrEmpty(uri.AbsolutePath) ||
            uri.AbsolutePath == "/" ||
            uri.AbsolutePath.EndsWith("/", StringComparison.Ordinal))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR087,
            assignment.Right.GetLocation(),
            uriText));
    }

    private static bool IsHttpClientReceiver(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        if (type is not null)
        {
            return HttpClientSymbols.IsHttpClient(type);
        }

        // Unresolved fallback: a visible local/parameter declared as HttpClient.
        return expression is IdentifierNameSyntax identifier &&
            RequestPathContext.VisibleIdentifierDeclarationType(identifier) is { } declaredType &&
            HttpClientSymbols.IsHttpClientName(declaredType);
    }
}
