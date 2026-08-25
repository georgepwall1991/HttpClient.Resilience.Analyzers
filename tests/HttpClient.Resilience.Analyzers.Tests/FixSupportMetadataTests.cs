using System.Collections.Immutable;
using System.Reflection;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.CodeFixes;
using Microsoft.CodeAnalysis.CodeFixes;
using Xunit;

namespace HttpClient.Resilience.Analyzers.Tests;

/// <summary>
/// Keeps the README rule catalog honest: a rule row may claim an automatic fix (Yes/Partial)
/// only when a shipped code fix provider actually fixes that diagnostic id, and every shipped
/// provider must be reflected in the catalog.
/// </summary>
public sealed class FixSupportMetadataTests
{
    private static string RepositoryRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string ReadmePath => Path.Combine(RepositoryRoot, "README.md");

    [Fact]
    public void Catalog_support_column_matches_shipped_code_fix_providers()
    {
        var readme = File.ReadAllText(ReadmePath);
        var providerIds = ShippedFixedDiagnosticIds();
        var catalogRows = ParseCatalogRows(readme);

        var failures = new List<string>();

        foreach (var (ruleId, support, line) in catalogRows)
        {
            var hasProvider = providerIds.Contains(ruleId);
            var claimsAutomatic = support is "Yes" or "Partial";

            if (hasProvider && !claimsAutomatic)
            {
                failures.Add($"{ruleId} has a shipped code fix but the catalog says '{support}'.");
            }

            if (!hasProvider && claimsAutomatic)
            {
                failures.Add($"{ruleId} claims '{support}' but no shipped provider fixes it.");
            }
        }

        foreach (var fixedId in providerIds)
        {
            if (catalogRows.All(row => row.RuleId != fixedId))
            {
                failures.Add($"{fixedId} is fixed by a shipped provider but missing from the catalog.");
            }
        }

        Assert.True(failures.Count == 0, string.Join(System.Environment.NewLine, failures));
    }

    private static ImmutableArray<string> ShippedFixedDiagnosticIds()
    {
        var ids = ImmutableArray.CreateBuilder<string>();
        foreach (var type in typeof(HCR060_DisposeResponseCodeFixProvider).Assembly.GetTypes())
        {
            if (!typeof(CodeFixProvider).IsAssignableFrom(type) || type.IsAbstract)
            {
                continue;
            }

            var instance = (CodeFixProvider)Activator.CreateInstance(type)!;
            foreach (var id in instance.FixableDiagnosticIds)
            {
                if (!ids.Contains(id))
                {
                    ids.Add(id);
                }
            }
        }

        return ids.ToImmutable();
    }

    private static List<(string RuleId, string Support, int Line)> ParseCatalogRows(string readme)
    {
        var rows = new List<(string RuleId, string Support, int Line)>();
        var lines = readme.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            var match = System.Text.RegularExpressions.Regex.Match(
                line,
                @"^\|\s*\[`(?<id>HCR\d+)`\].*?\|\s*(?<severity>Warning|Info)\s*\|\s*(?<support>[A-Za-z]+)\s*\|");
            if (match.Success)
            {
                rows.Add((match.Groups["id"].Value, match.Groups["support"].Value, index + 1));
            }
        }

        Assert.True(rows.Count >= 20, $"Expected the full rule catalog in {ReadmePath}, found {rows.Count} rows.");
        return rows;
    }
}
