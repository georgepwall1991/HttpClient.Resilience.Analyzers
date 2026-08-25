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
            var implementationTypeName = GetTypeName(genericName.TypeArgumentList.Arguments[1]);
            if (implementationTypeName.Length == 0)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    cancellationToken => AddExplicitNameAsync(
                        context.Document,
                        invocation,
                        implementationTypeName,
                        cancellationToken),
                    nameof(HCR085_AddExplicitClientNameCodeFixProvider)),
                diagnostic);
        }
    }

    private static async Task<Document> AddExplicitNameAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        string implementationTypeName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (root is null)
        {
            return document;
        }

        var nameLiteral = SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(ToKebabCase(implementationTypeName)));
        var nameArgument = SyntaxFactory.Argument(nameLiteral)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(
            invocation,
            invocation.WithArgumentList(invocation.ArgumentList.AddArguments(nameArgument))));
    }

    private static string GetTypeName(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => GetTypeName(qualified.Right),
            _ => string.Empty
        };
    }

    internal static string ToKebabCase(string typeName)
    {
        var builder = new StringBuilder(typeName.Length + 8);
        for (var index = 0; index < typeName.Length; index++)
        {
            var current = typeName[index];
            if (char.IsUpper(current) &&
                index > 0 &&
                (!char.IsUpper(typeName[index - 1]) ||
                    index + 1 < typeName.Length && char.IsLower(typeName[index + 1])))
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }
}
