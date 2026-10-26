using System.Text.Json;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Scan;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.Scan;

public sealed class ScanEndToEndTests
{
    [Fact]
    public void Scan_ReportsTheFindingAndWritesEveryReportFormat()
    {
        using var project = new TemporaryProject(
            """
            using System.Net.Http;

            namespace ScanSample;

            public static class ClientFactory
            {
                public static void Create()
                {
                    using var client = new HttpClient();
                }
            }
            """
        );
        var outputDirectory = Directory.CreateTempSubdirectory("hcr-scan-output-").FullName;
        try
        {
            var first = Run(
                project.Directory,
                "--sarif",
                Path.Combine(outputDirectory, "first.sarif"),
                "--html",
                Path.Combine(outputDirectory, "report.html"),
                "--summary",
                Path.Combine(outputDirectory, "report.md"),
                "--findings",
                "all"
            );
            var transcript = first.Output + Environment.NewLine + first.Error;

            Assert.Equal(ScanCommand.Success, first.ExitCode);
            Assert.True(
                first.Output.Contains(
                    "HCR Scan found 1 problem (1 rule, 1 file).",
                    StringComparison.Ordinal
                ),
                transcript
            );
            Assert.True(first.Output.Contains("HCR001", StringComparison.Ordinal), transcript);
            Assert.True(
                first.Output.Contains(
                    "using var client = new HttpClient();",
                    StringComparison.Ordinal
                ),
                transcript
            );

            using var sarif = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(outputDirectory, "first.sarif"))
            );
            var result = Assert.Single(
                sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            );
            Assert.Equal("HCR001", result.GetProperty("ruleId").GetString());
            Assert.Equal(
                "Program.cs",
                result
                    .GetProperty("locations")[0]
                    .GetProperty("physicalLocation")
                    .GetProperty("artifactLocation")
                    .GetProperty("uri")
                    .GetString()
            );

            var html = File.ReadAllText(Path.Combine(outputDirectory, "report.html"));
            Assert.Contains("HCR Scan report", html);
            Assert.Contains("using var client = new HttpClient();", html);
            Assert.Contains("HCR001", html);

            var markdown = File.ReadAllText(Path.Combine(outputDirectory, "report.md"));
            Assert.Contains("HCR Scan", markdown);
            Assert.Contains("HCR001", markdown);
            Assert.Contains("Program.cs", markdown);

            var second = Run(
                project.Directory,
                "--sarif",
                Path.Combine(outputDirectory, "second.sarif"),
                "--baseline",
                Path.Combine(outputDirectory, "first.sarif"),
                "--fail-on",
                "warning",
                "--no-restore"
            );

            Assert.Equal(ScanCommand.Success, second.ExitCode);
            Assert.Contains("HCR Scan found no new HttpClient resilience problems.", second.Output);
            Assert.Contains("1 finding already in the baseline is not listed", second.Output);

            var excluded = Run(
                project.Directory,
                "--sarif",
                Path.Combine(outputDirectory, "excluded.sarif"),
                "--exclude",
                "**/Program.cs",
                "--fail-on",
                "warning",
                "--no-restore"
            );

            Assert.Equal(ScanCommand.Success, excluded.ExitCode);
            Assert.Contains("HCR Scan found no HttpClient resilience problems.", excluded.Output);
            Assert.Contains(
                "1 finding left out by --rules, --skip-rules or --exclude.",
                excluded.Output
            );
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public void Scan_FailsWhenAFindingMeetsTheFailOnSeverity()
    {
        using var project = new TemporaryProject(
            """
            using System.Net.Http;

            public static class ClientFactory
            {
                public static void Create()
                {
                    using var client = new HttpClient();
                }
            }
            """
        );

        var result = Run(project.Directory, "--fail-on", "warning");

        Assert.Equal(ScanCommand.FindingsFound, result.ExitCode);
        Assert.Contains(
            "Failing: 1 finding is warning severity or higher (--fail-on warning).",
            result.Error
        );
    }

    [Fact]
    public void Scan_FixAppliesTheCodeFixAndLeavesACompilingProject()
    {
        using var project = new TemporaryProject(
            """
            using System.Net.Http;

            namespace ScanSample;

            public sealed class PaymentsService(IHttpClientFactory factory)
            {
                public HttpClient Create()
                {
                    _ = factory;
                    return new HttpClient();
                }
            }

            public interface IHttpClientFactory
            {
                HttpClient CreateClient(string name);
            }
            """
        );

        var result = Run(project.Directory, "--fix", "--rules", "HCR001");

        var transcript = result.Output + Environment.NewLine + result.Error;
        Assert.Equal(ScanCommand.Success, result.ExitCode);
        Assert.True(
            result.Output.Contains(
                "Applied fixes to 1 file. Review them with 'git diff' before you commit:",
                StringComparison.Ordinal
            ),
            transcript
        );

        var fixedSource = File.ReadAllText(Path.Combine(project.Directory, "Program.cs"));
        Assert.Contains(
            "factory.CreateClient(string.Empty)",
            fixedSource,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("new HttpClient()", fixedSource, StringComparison.Ordinal);

        // The scan that follows the fix only stays clean because the rewritten source compiles.
        Assert.True(
            result.Output.Contains(
                "HCR Scan found no HttpClient resilience problems.",
                StringComparison.Ordinal
            ),
            transcript
        );
    }

    private static (int ExitCode, string Output, string Error) Run(
        string target,
        params string[] arguments
    )
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = ScanCommand.Run(
            [target, "--no-github", .. arguments],
            output,
            error,
            BuiltAnalyzerPath(),
            environment: _ => null
        );
        return (exitCode, output.ToString(), error.ToString());
    }

    private static string BuiltAnalyzerPath()
    {
        var root = DiagnosticSnapshot.TryFindRepositoryRoot()!;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Release";
        var built = Path.Combine(
            root,
            "src",
            "HttpClient.Resilience.Analyzers",
            "bin",
            configuration,
            "netstandard2.0",
            "HttpClient.Resilience.Analyzers.dll"
        );
        return File.Exists(built) ? built : typeof(DiagnosticIds).Assembly.Location;
    }

    private sealed class TemporaryProject : IDisposable
    {
        public TemporaryProject(string source)
        {
            Directory = System.IO.Directory.CreateTempSubdirectory("hcr-scan-project-").FullName;
            File.WriteAllText(
                Path.Combine(Directory, "ScanSample.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """
            );
            File.WriteAllText(Path.Combine(Directory, "Program.cs"), source);
        }

        public string Directory { get; }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
