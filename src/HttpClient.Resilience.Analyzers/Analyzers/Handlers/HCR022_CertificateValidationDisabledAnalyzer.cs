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
public sealed class HCR022_CertificateValidationDisabledAnalyzer : DiagnosticAnalyzer
{
    private const string CallbackPropertyName = "ServerCertificateCustomValidationCallback";
    private const string DangerousValidatorName = "DangerousAcceptAnyServerCertificateValidator";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR022);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        if (!LeftTargetsCertificateValidationCallback(assignment.Left, context.SemanticModel, context.CancellationToken) ||
            !RightDisablesCertificateValidation(assignment.Right, context.SemanticModel, context.CancellationToken) ||
            RequestPathContext.IsInTestContext(assignment))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR022,
            assignment.Right.GetLocation()));
    }

    private static bool LeftTargetsCertificateValidationCallback(
        ExpressionSyntax left,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var property = semanticModel.GetSymbolInfo(left, cancellationToken).Symbol as IPropertySymbol;
        if (property is not null)
        {
            return property.Name == CallbackPropertyName &&
                IsHttpHandlerType(property.ContainingType);
        }

        // Unresolved fallback: only a member access on a visibly handler-typed receiver
        // or an object-initializer identifier counts — a bare same-named property on an
        // unrelated type is not framework evidence.
        return left switch
        {
            MemberAccessExpressionSyntax memberAccess =>
                memberAccess.Name.Identifier.ValueText == CallbackPropertyName &&
                ReceiverLooksLikeHttpHandler(memberAccess.Expression),
            IdentifierNameSyntax identifier =>
                identifier.Identifier.ValueText == CallbackPropertyName &&
                identifier.FirstAncestorOrSelf<InitializerExpressionSyntax>()?.Parent
                    is ObjectCreationExpressionSyntax creation &&
                TypeNameLooksLikeHttpHandler(creation.Type),
            _ => false
        };
    }

    private static bool RightDisablesCertificateValidation(
        ExpressionSyntax right,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        right = SyntaxTransparency.Unwrap(right);

        if (right is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
        {
            return BodyReturnsConstantTrue(right);
        }

        // HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        var symbol = semanticModel.GetSymbolInfo(right, cancellationToken).Symbol;
        if (symbol is IPropertySymbol validator)
        {
            return validator.Name == DangerousValidatorName &&
                IsHttpHandlerType(validator.ContainingType);
        }

        return right is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.ValueText == DangerousValidatorName &&
            memberAccess.Expression is IdentifierNameSyntax owner &&
            owner.Identifier.ValueText == "HttpClientHandler";
    }

    private static bool BodyReturnsConstantTrue(ExpressionSyntax function)
    {
        return function switch
        {
            LambdaExpressionSyntax { Body: ExpressionSyntax expression } => IsTrueLiteral(expression),
            LambdaExpressionSyntax { Body: BlockSyntax block } => BlockOnlyReturnsTrue(block),
            AnonymousMethodExpressionSyntax { Block: { } block } => BlockOnlyReturnsTrue(block),
            _ => false
        };
    }

    private static bool BlockOnlyReturnsTrue(BlockSyntax block)
    {
        return block.Statements.Count == 1 &&
            block.Statements[0] is ReturnStatementSyntax returnStatement &&
            returnStatement.Expression is { } expression &&
            IsTrueLiteral(expression);
    }

    private static bool IsTrueLiteral(ExpressionSyntax expression)
    {
        expression = SyntaxTransparency.Unwrap(expression);
        return expression.IsKind(SyntaxKind.TrueLiteralExpression);
    }

    private static bool IsHttpHandlerType(INamedTypeSymbol? type)
    {
        return type is not null &&
            type.Name is "HttpClientHandler" or "WinHttpHandler" &&
            type.ContainingNamespace.ToDisplayString() == "System.Net.Http";
    }

    private static bool ReceiverLooksLikeHttpHandler(ExpressionSyntax receiver)
    {
        return receiver switch
        {
            IdentifierNameSyntax identifier =>
                RequestPathContext.VisibleIdentifierDeclarationType(identifier) is { } type &&
                TypeNameLooksLikeHttpHandler(type),
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax name } =>
                RequestPathContext.VisibleIdentifierDeclarationType(name) is { } type &&
                TypeNameLooksLikeHttpHandler(type),
            ObjectCreationExpressionSyntax creation => TypeNameLooksLikeHttpHandler(creation.Type),
            _ => false
        };
    }

    private static bool TypeNameLooksLikeHttpHandler(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText is
                "HttpClientHandler" or "WinHttpHandler",
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText is
                "HttpClientHandler" or "WinHttpHandler",
            _ => false
        };
    }
}
