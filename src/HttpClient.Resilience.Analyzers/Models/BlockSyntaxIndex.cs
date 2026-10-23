using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HttpClient.Resilience.Analyzers.Models;

/// <summary>
/// Caches the descendant-node projections that ownership analyses ask for repeatedly.
/// Building once per block keeps disposal and stream analyses linear in the block size.
/// </summary>
internal sealed class BlockSyntaxIndex
{
    private static readonly ConditionalWeakTable<BlockSyntax, BlockSyntaxIndex> Cache = new();

    private BlockSyntaxIndex(BlockSyntax block)
    {
        var nodes = block.DescendantNodes().ToArray();
        Returns = nodes.OfType<ReturnStatementSyntax>().ToImmutableArray();
        Invocations = nodes.OfType<InvocationExpressionSyntax>().ToImmutableArray();
        FinallyClauses = nodes.OfType<FinallyClauseSyntax>().ToImmutableArray();
        UsingStatements = nodes.OfType<UsingStatementSyntax>().ToImmutableArray();
        LocalDeclarations = nodes.OfType<LocalDeclarationStatementSyntax>().ToImmutableArray();
        Assignments = nodes.OfType<AssignmentExpressionSyntax>().ToImmutableArray();
        Variables = nodes.OfType<VariableDeclaratorSyntax>().ToImmutableArray();
    }

    public static BlockSyntaxIndex Get(BlockSyntax block)
    {
        return Cache.GetValue(block, key => new BlockSyntaxIndex(key));
    }

    public ImmutableArray<ReturnStatementSyntax> Returns { get; }

    public ImmutableArray<InvocationExpressionSyntax> Invocations { get; }

    public ImmutableArray<FinallyClauseSyntax> FinallyClauses { get; }

    public ImmutableArray<UsingStatementSyntax> UsingStatements { get; }

    public ImmutableArray<LocalDeclarationStatementSyntax> LocalDeclarations { get; }

    public ImmutableArray<AssignmentExpressionSyntax> Assignments { get; }

    public ImmutableArray<VariableDeclaratorSyntax> Variables { get; }
}
