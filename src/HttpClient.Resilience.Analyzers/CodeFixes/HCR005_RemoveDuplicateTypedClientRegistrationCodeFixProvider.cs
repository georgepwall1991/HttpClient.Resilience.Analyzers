using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HCR005_RemoveDuplicateTypedClientRegistrationCodeFixProvider))]
[Shared]
public sealed class HCR005_RemoveDuplicateTypedClientRegistrationCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DiagnosticIds.HCR005);

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
            var statement = node.FirstAncestorOrSelf<ExpressionStatementSyntax>();
            var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();

            if (statement is null || invocation is null || !CanSafelyRemove(statement, invocation))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Remove duplicate typed-client registration",
                    cancellationToken => RemoveStatementAsync(context.Document, statement, cancellationToken),
                    nameof(HCR005_RemoveDuplicateTypedClientRegistrationCodeFixProvider)),
                diagnostic);
        }
    }

    private static bool CanSafelyRemove(
        ExpressionStatementSyntax statement,
        InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList.Arguments.Count != 0)
        {
            return false;
        }

        var expression = statement.Expression;
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression == invocation;
    }

    private static async Task<Document> RemoveStatementAsync(
        Document document,
        ExpressionStatementSyntax statement,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var marker = new SyntaxAnnotation();
        var annotatedRoot = root.ReplaceNode(statement, statement.WithAdditionalAnnotations(marker));
        var annotatedStatement = (StatementSyntax)annotatedRoot.GetAnnotatedNodesAndTokens(marker).Single().AsNode()!;

        var significantTrivia = CollectCommentBlock(annotatedStatement.GetLeadingTrivia());
        significantTrivia = CollectCommentBlock(
            significantTrivia.AddRange(annotatedStatement.GetLastToken().TrailingTrivia));
        if (!significantTrivia.Any() || !significantTrivia.Last().IsKind(SyntaxKind.EndOfLineTrivia))
        {
            significantTrivia = significantTrivia.Add(SyntaxFactory.EndOfLine("\r\n"));
        }

        var migratedRoot = annotatedRoot;
        if (significantTrivia.Count > 0 && annotatedStatement.Parent is BlockSyntax block)
        {
            var index = block.Statements.IndexOf(annotatedStatement);

            if (index >= 0 && index + 1 < block.Statements.Count)
            {
                var following = block.Statements[index + 1];
                migratedRoot = annotatedRoot.ReplaceNode(
                    following,
                    following.WithLeadingTrivia(
                        SyntaxFactory.TriviaList(significantTrivia).AddRange(following.GetLeadingTrivia())));
            }
            else
            {
                var closeBrace = block.CloseBraceToken;
                migratedRoot = annotatedRoot.ReplaceToken(
                    closeBrace,
                    closeBrace.WithLeadingTrivia(
                        SyntaxFactory.TriviaList(significantTrivia).AddRange(closeBrace.LeadingTrivia)));
            }
        }

        var statementToRemove = (StatementSyntax)migratedRoot.GetAnnotatedNodesAndTokens(marker).Single().AsNode()!;
        var newRoot = migratedRoot.RemoveNode(statementToRemove, SyntaxRemoveOptions.KeepNoTrivia);
        if (newRoot is null)
        {
            return document;
        }

        return document.WithSyntaxRoot(newRoot.WithAdditionalAnnotations(Formatter.Annotation));
    }

    private static SyntaxTriviaList CollectCommentBlock(SyntaxTriviaList trivia)
    {
        var collected = SyntaxFactory.TriviaList();
        foreach (var item in trivia)
        {
            if (IsSignificantComment(item))
            {
                collected = collected.Add(item);
            }
            else if (item.IsKind(SyntaxKind.EndOfLineTrivia) &&
                collected.Any() && IsSignificantComment(collected.Last()))
            {
                collected = collected.Add(item);
            }
        }

        return collected;
    }


    private static bool IsSignificantComment(SyntaxTrivia trivia)
    {
        return trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
            trivia.IsKind(SyntaxKind.MultiLineCommentTrivia);
    }
}
