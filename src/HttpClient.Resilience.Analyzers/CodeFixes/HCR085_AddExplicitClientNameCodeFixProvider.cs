using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HttpClient.Resilience.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace HttpClient.Resilience.Analyzers.CodeFixes;

/// <summary>
/// Adds an explicit, distinct client name — derived from the implementation type in
/// kebab-case — to a flagged <c>AddHttpClient&lt;TService, TImplementation&gt;()</c> call so the
/// registrations no longer share the implicit name.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HCR085_AddExplicitClientNameCodeFixProvider))]
[Shared]
public sealed class HCR085_AddExplicitClientNameCodeFixProvider : CodeFixProvider
{
    public const string Title = "Add explicit client name";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DiagnosticIds.HCR085);

    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan);
            var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();

            if (invocation?.Expression is not MemberAccessExpressionSyntax
                {
                    Name: GenericNameSyntax { TypeArgumentList.Arguments.Count: >= 1 } genericName
                } ||
                genericName.TypeArgumentList.Arguments.Count < 2)
            {
                continue;
            }
            var implementationType = genericName.TypeArgumentList.Arguments[1];

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    cancellationToken => AddExplicitNameAsync(
                        context.Document,
                        invocation,
                        implementationType,
                        cancellationToken),
                    nameof(HCR085_AddExplicitClientNameCodeFixProvider)),
                diagnostic);
        }
    }

    private static async Task<Document> AddExplicitNameAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        TypeSyntax implementationType,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null)
        {
            return document;
        }

        var type = semanticModel.GetTypeInfo(implementationType, cancellationToken).Type;
        if (type is null || type is IErrorTypeSymbol)
        {
            return document;
        }

        // The fully qualified name keeps names distinct even when two implementations share
        // a leaf name (OuterA.Client vs OuterB.Client) or differ only by namespace.
        var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var nameLiteral = SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(ToKebabCase(fullName)));
        var nameArgument = SyntaxFactory.Argument(nameLiteral)
            .WithAdditionalAnnotations(Formatter.Annotation);

        // Framework overloads expect the name first: (string) or (string, Action<HttpClient>).
        return document.WithSyntaxRoot(root.ReplaceNode(
            invocation,
            invocation.WithArgumentList(invocation.ArgumentList.WithArguments(
                invocation.ArgumentList.Arguments.Insert(0, nameArgument)))));
    }

    internal static string ToKebabCase(string typeName)
    {
        if (typeName.StartsWith("global::", System.StringComparison.Ordinal))
        {
            typeName = typeName.Substring("global::".Length);
        }

        var builder = new StringBuilder(typeName.Length + 8);
        var previousOriginal = '\0';
        for (var index = 0; index < typeName.Length; index++)
        {
            var current = typeName[index];
            if (!char.IsLetterOrDigit(current))
            {
                if (builder.Length > 0 && builder[builder.Length - 1] != '-')
                {
                    builder.Append('-');
                }

                previousOriginal = '-';
                continue;
            }

            var nextIsLower = index + 1 < typeName.Length && char.IsLower(typeName[index + 1]);
            if (char.IsUpper(current) &&
                builder.Length > 0 &&
                builder[builder.Length - 1] != '-' &&
                (!char.IsUpper(previousOriginal) || nextIsLower))
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(current));
            previousOriginal = current;
        }

        return builder.ToString();
    }
}
