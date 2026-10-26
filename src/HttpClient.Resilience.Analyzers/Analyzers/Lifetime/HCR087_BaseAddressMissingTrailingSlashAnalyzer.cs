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
        if (RequestPathContext.IsInTestContext(assignment) ||
            !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            !IsHttpClientBaseAddressAssignment(
                assignment,
                context.SemanticModel,
                context.CancellationToken))
        {
            return;
        }

        if (!TryGetUriText(
                assignment.Right,
                context.SemanticModel,
                context.CancellationToken,
                out var uriText))
        {
            return;
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

    private static bool IsHttpClientBaseAddressAssignment(
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (assignment.Left is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "BaseAddress"
            } memberAccess)
        {
            return IsHttpClientReceiver(memberAccess.Expression, semanticModel, cancellationToken);
        }

        // new HttpClient { BaseAddress = new Uri("...") }
        if (assignment.Left is IdentifierNameSyntax { Identifier.ValueText: "BaseAddress" } &&
            assignment.Parent is InitializerExpressionSyntax
            {
                Parent: BaseObjectCreationExpressionSyntax objectCreation
            } initializer &&
            initializer.IsKind(SyntaxKind.ObjectInitializerExpression))
        {
            var createdType = semanticModel.GetTypeInfo(objectCreation, cancellationToken).Type;
            return createdType is not null && HttpClientSymbols.IsHttpClient(createdType);
        }

        return false;
    }

    private static bool TryGetUriText(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out string uriText)
    {
        expression = UnwrapTransparentExpressions(expression);

        var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
        if (constantValue is { HasValue: true, Value: string text })
        {
            uriText = text;
            return true;
        }

        // new Uri("literal") or new Uri(BaseAddressConstant). Both explicit and
        // target-typed `new(...)` creations are accepted.
        if (expression is BaseObjectCreationExpressionSyntax creation &&
            creation.ArgumentList is { Arguments.Count: >= 1 and <= 2 } arguments &&
            IsSystemUri(semanticModel.GetTypeInfo(creation, cancellationToken).Type))
        {
            var argumentValue = semanticModel.GetConstantValue(
                arguments.Arguments[0].Expression,
                cancellationToken);
            if (argumentValue is { HasValue: true, Value: string argumentText })
            {
                uriText = argumentText;
                return true;
            }
        }

        uriText = string.Empty;
        return false;
    }

    private static bool IsSystemUri(ITypeSymbol? type)
    {
        return type is not null &&
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Uri";
    }

    private static ExpressionSyntax UnwrapTransparentExpressions(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax postfix
                    when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = postfix.Operand;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private static bool IsHttpClientReceiver(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        if (type is not null && type is not IErrorTypeSymbol)
        {
            return HttpClientSymbols.IsHttpClient(type);
        }

        // Unresolved fallback: a visible local/parameter declared as HttpClient.
        return expression is IdentifierNameSyntax identifier &&
            RequestPathContext.VisibleIdentifierDeclarationType(identifier) is { } declaredType &&
            HttpClientSymbols.IsHttpClientName(declaredType);
    }
}
