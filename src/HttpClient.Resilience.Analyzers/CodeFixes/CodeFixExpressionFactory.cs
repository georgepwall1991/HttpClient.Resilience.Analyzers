using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HttpClient.Resilience.Analyzers.CodeFixes;

/// <summary>
/// Builds well-known expressions used by resilience code fixes as structured syntax trees.
/// Structured construction avoids re-parsing source text on every fix application.
/// </summary>
internal static class CodeFixExpressionFactory
{
    private static readonly ExpressionSyntax DisableForUnsafeHttpMethodsLambda =
        BuildDisableForUnsafeHttpMethodsLambda();

    /// <summary>
    /// Builds <c>options => options.Retry.DisableForUnsafeHttpMethods()</c>. The resulting tree
    /// is immutable and cached, so repeated fix applications share one instance.
    /// </summary>
    public static ExpressionSyntax CreateDisableForUnsafeHttpMethodsLambda() =>
        DisableForUnsafeHttpMethodsLambda;

    private static ExpressionSyntax BuildDisableForUnsafeHttpMethodsLambda()
    {
        return SyntaxFactory.SimpleLambdaExpression(
            SyntaxFactory.Parameter(SyntaxFactory.Identifier("options")),
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName("options"),
                        SyntaxFactory.IdentifierName("Retry")),
                    SyntaxFactory.IdentifierName("DisableForUnsafeHttpMethods"))));
    }
}
