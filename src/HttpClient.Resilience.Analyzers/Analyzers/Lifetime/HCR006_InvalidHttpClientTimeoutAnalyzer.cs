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
public sealed class HCR006_InvalidHttpClientTimeoutAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR006);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        if (!LeftTargetsHttpClientTimeout(assignment.Left, context.SemanticModel, context.CancellationToken) ||
            !RightIsInvalidTimeout(assignment.Right, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR006,
            assignment.Right.GetLocation()));
    }

    private static bool LeftTargetsHttpClientTimeout(
        ExpressionSyntax left,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var property = semanticModel.GetSymbolInfo(left, cancellationToken).Symbol as IPropertySymbol;
        if (property is not null)
        {
            return property.Name == "Timeout" &&
                HttpClientSymbols.IsHttpClient(property.ContainingType);
        }

        // Unresolved fallback: member access on a visibly HttpClient-typed receiver, or
        // an object-initializer identifier inside a visible HttpClient creation.
        return left switch
        {
            MemberAccessExpressionSyntax memberAccess =>
                memberAccess.Name.Identifier.ValueText == "Timeout" &&
                ReceiverLooksLikeHttpClient(memberAccess.Expression),
            IdentifierNameSyntax identifier =>
                identifier.Identifier.ValueText == "Timeout" &&
                identifier.FirstAncestorOrSelf<InitializerExpressionSyntax>()?.Parent
                    is ObjectCreationExpressionSyntax creation &&
                HttpClientSymbols.IsHttpClientName(creation.Type),
            _ => false
        };
    }

    private static bool RightIsInvalidTimeout(
        ExpressionSyntax right,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        right = SyntaxTransparency.Unwrap(right);

        // The Timeout setter throws ArgumentOutOfRangeException for any value that is
        // not positive and not Timeout.InfiniteTimeSpan — so zero and negative values
        // are guaranteed crashes. InfiniteTimeSpan is deliberately not flagged: it is
        // the documented way to delegate timeouts to a resilience pipeline.
        return right switch
        {
            // TimeSpan.Zero, default(TimeSpan), default
            MemberAccessExpressionSyntax memberAccess =>
                memberAccess.Name.Identifier.ValueText == "Zero" &&
                ExpressionIsTimeSpanType(memberAccess.Expression, semanticModel, cancellationToken),
            DefaultExpressionSyntax defaultExpression =>
                TypeIsTimeSpan(defaultExpression.Type, semanticModel, cancellationToken),
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.DefaultLiteralExpression) => true,
            // new TimeSpan(...) with a non-positive constant ticks argument
            ObjectCreationExpressionSyntax creation =>
                TypeIsTimeSpan(creation.Type, semanticModel, cancellationToken) &&
                CreationHasNonPositiveConstantArgument(creation, semanticModel, cancellationToken),
            // TimeSpan.FromX(non-positive constant)
            InvocationExpressionSyntax invocation =>
                IsTimeSpanFactoryWithNonPositiveArgument(invocation, semanticModel, cancellationToken),
            PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.UnaryMinusExpression) =>
                IsNegativeTimeSpanExpression(prefix, semanticModel, cancellationToken),
            _ => false
        };
    }

    private static bool CreationHasNonPositiveConstantArgument(
        ObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (creation.ArgumentList is null || creation.ArgumentList.Arguments.Count == 0)
        {
            // new TimeSpan() is zero ticks.
            return true;
        }

        // Every TimeSpan constructor takes numeric arguments; a non-positive first
        // argument produces a non-positive TimeSpan.
        return TryGetNumericConstant(
            creation.ArgumentList.Arguments[0].Expression,
            semanticModel,
            cancellationToken,
            out var value) && value <= 0;
    }

    private static bool IsTimeSpanFactoryWithNonPositiveArgument(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name.Identifier.ValueText is not (
                "FromDays" or "FromHours" or "FromMilliseconds" or "FromMinutes" or
                "FromSeconds" or "FromTicks" or "FromMicroseconds") ||
            !ExpressionIsTimeSpanType(memberAccess.Expression, semanticModel, cancellationToken) ||
            invocation.ArgumentList.Arguments.Count == 0)
        {
            return false;
        }

        return TryGetNumericConstant(
            invocation.ArgumentList.Arguments[0].Expression,
            semanticModel,
            cancellationToken,
            out var value) && value <= 0;
    }

    private static bool IsNegativeTimeSpanExpression(
        PrefixUnaryExpressionSyntax prefix,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        // -TimeSpan.FromSeconds(5) — a negated positive factory result.
        var operand = SyntaxTransparency.Unwrap(prefix.Operand);
        return operand is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.ValueText.StartsWith("From", System.StringComparison.Ordinal) &&
            ExpressionIsTimeSpanType(memberAccess.Expression, semanticModel, cancellationToken) &&
            invocation.ArgumentList.Arguments.Count > 0 &&
            TryGetNumericConstant(
                invocation.ArgumentList.Arguments[0].Expression,
                semanticModel,
                cancellationToken,
                out var value) && value > 0;
    }

    private static bool TryGetNumericConstant(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out double value)
    {
        expression = SyntaxTransparency.Unwrap(expression);
        var constant = semanticModel.GetConstantValue(expression, cancellationToken);
        if (constant.HasValue)
        {
            switch (constant.Value)
            {
                case int intValue: value = intValue; return true;
                case long longValue: value = longValue; return true;
                case double doubleValue: value = doubleValue; return true;
                case float floatValue: value = floatValue; return true;
                case short shortValue: value = shortValue; return true;
                case byte byteValue: value = byteValue; return true;
            }
        }

        // Unresolved fallback for literal arguments.
        if (expression is LiteralExpressionSyntax literal)
        {
            var text = literal.Token.ValueText;
            if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool ExpressionIsTimeSpanType(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
        if (symbol is INamedTypeSymbol namedType)
        {
            return IsTimeSpan(namedType);
        }

        return expression is IdentifierNameSyntax identifier &&
            identifier.Identifier.ValueText == "TimeSpan" ||
            expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.ValueText == "TimeSpan" &&
            memberAccess.Expression.ToString() is "System" or "global::System";
    }

    private static bool TypeIsTimeSpan(
        TypeSyntax type,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var resolved = semanticModel.GetTypeInfo(type, cancellationToken).Type;
        if (resolved is not null && resolved is not IErrorTypeSymbol)
        {
            return IsTimeSpan(resolved);
        }

        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "TimeSpan",
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText == "TimeSpan",
            _ => false
        };
    }

    private static bool IsTimeSpan(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::System.TimeSpan";
    }

    private static bool ReceiverLooksLikeHttpClient(ExpressionSyntax receiver)
    {
        return receiver is IdentifierNameSyntax identifier &&
            RequestPathContext.VisibleIdentifierDeclarationType(identifier) is { } type &&
            HttpClientSymbols.IsHttpClientName(type);
    }
}
