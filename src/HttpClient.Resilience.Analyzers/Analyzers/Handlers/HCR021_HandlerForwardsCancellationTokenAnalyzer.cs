using System.Collections.Immutable;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.Handlers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR021_HandlerForwardsCancellationTokenAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR021);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Expression: BaseExpressionSyntax,
                Name.Identifier.ValueText: "SendAsync"
            })
        {
            return;
        }

        var method = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method is null ||
            method.Identifier.ValueText != "SendAsync" ||
            !method.Modifiers.Any(SyntaxKind.OverrideKeyword) ||
            !DerivesFromDelegatingHandler(method, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count >= 2 && !IsNonCancellingToken(arguments[1].Expression))
        {
            return;
        }

        // Only flag when the override actually receives a token it could forward.
        if (!method.ParameterList.Parameters.Any(parameter =>
                ParameterIsCancellationToken(parameter, context.SemanticModel, context.CancellationToken)))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR021,
            arguments.Count >= 2
                ? arguments[1].GetLocation()
                : invocation.GetLocation()));
    }

    private static bool IsNonCancellingToken(ExpressionSyntax expression)
    {
        expression = SyntaxTransparency.Unwrap(expression);

        return expression switch
        {
            // CancellationToken.None
            MemberAccessExpressionSyntax memberAccess =>
                memberAccess.Name.Identifier.ValueText == "None" &&
                memberAccess.Expression is IdentifierNameSyntax owner &&
                owner.Identifier.ValueText == "CancellationToken",
            // default / default(CancellationToken)
            LiteralExpressionSyntax literal => literal.IsKind(SyntaxKind.DefaultLiteralExpression),
            DefaultExpressionSyntax => true,
            // new CancellationToken()
            ObjectCreationExpressionSyntax creation =>
                creation.ArgumentList is null || creation.ArgumentList.Arguments.Count == 0,
            ImplicitObjectCreationExpressionSyntax => true,
            _ => false
        };
    }

    private static bool DerivesFromDelegatingHandler(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (semanticModel.GetDeclaredSymbol(method, cancellationToken) is IMethodSymbol methodSymbol)
        {
            for (var type = methodSymbol.ContainingType?.BaseType;
                type is not null;
                type = type.BaseType)
            {
                if (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                    "global::System.Net.Http.DelegatingHandler")
                {
                    return true;
                }
            }

            return false;
        }

        // Unresolved fallback: the containing type must visibly declare a
        // DelegatingHandler base.
        return method.FirstAncestorOrSelf<TypeDeclarationSyntax>()?
            .BaseList?.Types.Any(type =>
                type.Type is IdentifierNameSyntax identifier &&
                    identifier.Identifier.ValueText == "DelegatingHandler" ||
                type.Type is QualifiedNameSyntax qualified &&
                    qualified.ToString() is "System.Net.Http.DelegatingHandler" or "global::System.Net.Http.DelegatingHandler") == true;
    }

    private static bool ParameterIsCancellationToken(
        ParameterSyntax parameter,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (semanticModel.GetDeclaredSymbol(parameter, cancellationToken) is IParameterSymbol symbol)
        {
            return symbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                "global::System.Threading.CancellationToken";
        }

        return parameter.Type is not null &&
            TypeNameLooksLikeCancellationToken(parameter.Type);
    }

    private static bool TypeNameLooksLikeCancellationToken(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "CancellationToken",
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText == "CancellationToken",
            _ => false
        };
    }
}
