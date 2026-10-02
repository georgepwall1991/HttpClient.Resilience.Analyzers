using Microsoft.CodeAnalysis;
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

    /// <summary>
    /// Creates an identifier name that stays valid when the source name is a keyword
    /// (for example a lambda parameter declared as <c>@event</c>).
    /// </summary>
    public static IdentifierNameSyntax CreateIdentifierName(string name) =>
        SyntaxFactory.IdentifierName(CreateIdentifier(name));

    /// <summary>
    /// Creates an identifier token that stays valid when the source name is a keyword.
    /// The emitted text is verbatim (<c>@event</c>) while the value text stays the
    /// bare name so the identifier still binds to the original declaration.
    /// </summary>
    public static SyntaxToken CreateIdentifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None
            ? SyntaxFactory.Identifier(name)
            : SyntaxFactory.Identifier(
                SyntaxTriviaList.Empty,
                SyntaxKind.IdentifierToken,
                "@" + name,
                name,
                SyntaxTriviaList.Empty);

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
