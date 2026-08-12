using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace HttpClient.Resilience.Analyzers.Models;

/// <summary>
/// Per-compilation syntax data that several analyzers need in full. Materializing the roots
/// once and sharing them keeps a solution build from repeating the same forest walk for
/// every compilation-wide rule.
/// </summary>
/// <remarks>
/// Keyed by <see cref="Compilation"/> through a <see cref="ConditionalWeakTable{TKey,TValue}"/>,
/// so the cache dies with the compilation it describes. Only cheap derived data is retained;
/// bulk node lists are deliberately not cached, because holding them alive would trade a
/// modest CPU saving for a real memory regression in a long-running IDE session.
/// </remarks>
internal sealed class CompilationSyntaxIndex
{
    private static readonly ConditionalWeakTable<Compilation, CompilationSyntaxIndex> Cache = new();

    private readonly object _gate = new();
    private readonly Compilation _compilation;
    private IReadOnlyList<SyntaxNode>? _roots;
    private int _rootMaterializations;

    private CompilationSyntaxIndex(Compilation compilation)
    {
        _compilation = compilation;
    }

    public static CompilationSyntaxIndex GetOrCreate(Compilation compilation)
    {
        return Cache.GetValue(compilation, key => new CompilationSyntaxIndex(key));
    }

    /// <summary>
    /// Syntax roots in <see cref="Compilation.SyntaxTrees"/> order, which is the traversal
    /// order every caller previously produced for itself.
    /// </summary>
    public static IReadOnlyList<SyntaxNode> GetRoots(
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        return GetOrCreate(compilation).GetRoots(cancellationToken);
    }

    /// <summary>
    /// How many times the roots of <paramref name="compilation"/> have been materialized.
    /// Tests use it to assert the shared index is not rebuilt per analyzer.
    /// </summary>
    internal static int GetRootMaterializationCount(Compilation compilation)
    {
        return Cache.TryGetValue(compilation, out var index)
            ? System.Threading.Volatile.Read(ref index._rootMaterializations)
            : 0;
    }

    private IReadOnlyList<SyntaxNode> GetRoots(System.Threading.CancellationToken cancellationToken)
    {
        if (System.Threading.Volatile.Read(ref _roots) is { } published)
        {
            return published;
        }

        lock (_gate)
        {
            if (_roots is { } cached)
            {
                return cached;
            }

            // A cancelled materialization throws before publishing, so a partial list is
            // never cached.
            var roots = _compilation.SyntaxTrees
                .Select(tree => tree.GetRoot(cancellationToken))
                .ToArray();

            System.Threading.Interlocked.Increment(ref _rootMaterializations);
            System.Threading.Volatile.Write(ref _roots, roots);
            return roots;
        }
    }
}
