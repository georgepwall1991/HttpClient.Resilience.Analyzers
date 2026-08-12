using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

/// <summary>
/// Renders analyzer diagnostics into a stable, human-readable text form so a whole
/// compilation's behavior can be compared against a committed baseline.
/// </summary>
internal static class DiagnosticSnapshot
{
    /// <summary>
    /// Set to <c>1</c> to rewrite the committed baseline from the current analyzer output.
    /// </summary>
    public const string UpdateEnvironmentVariable = "HCR_UPDATE_SNAPSHOTS";

    public static string Render(IEnumerable<Diagnostic> diagnostics)
    {
        var lines = diagnostics
            .Select(diagnostic => (Diagnostic: diagnostic, Position: diagnostic.Location.GetLineSpan()))
            .OrderBy(entry => Path.GetFileName(entry.Position.Path), StringComparer.Ordinal)
            .ThenBy(entry => entry.Position.StartLinePosition.Line)
            .ThenBy(entry => entry.Position.StartLinePosition.Character)
            .ThenBy(entry => entry.Diagnostic.Id, StringComparer.Ordinal)
            .Select(entry => RenderLine(entry.Diagnostic))
            .ToImmutableArray();

        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }

    public static bool ShouldUpdateBaseline()
    {
        return Environment.GetEnvironmentVariable(UpdateEnvironmentVariable) == "1";
    }

    /// <summary>
    /// Locates the repository root by walking up from the test assembly until the solution
    /// file appears. Only used when regenerating a baseline.
    /// </summary>
    public static string? TryFindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HttpClient.Resilience.Analyzers.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    public static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static string RenderLine(Diagnostic diagnostic)
    {
        var lineSpan = diagnostic.Location.GetLineSpan();
        var file = Path.GetFileName(lineSpan.Path);
        var position = lineSpan.StartLinePosition;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{file}({position.Line + 1},{position.Character + 1}): {diagnostic.Severity.ToString().ToLowerInvariant()} {diagnostic.Id}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}");
    }
}
