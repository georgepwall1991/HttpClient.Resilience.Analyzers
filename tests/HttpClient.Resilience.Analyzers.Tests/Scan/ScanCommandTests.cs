using HttpClient.Resilience.Analyzers.Scan;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.Scan;

public sealed class ScanCommandTests
{
    private static (int ExitCode, string Output, string Error) Run(
        string[] args,
        string? analyzerPath = null
    )
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = ScanCommand.Run(args, output, error, analyzerPath, environment: _ => null);
        return (exitCode, output.ToString(), error.ToString());
    }

    [Fact]
    public void Help_PrintsUsage()
    {
        var (exitCode, output, _) = Run(["--help"]);

        Assert.Equal(ScanCommand.Success, exitCode);
        Assert.Contains("Usage: hcr-scan", output);
        Assert.Contains("--fail-on", output);
    }

    [Fact]
    public void Version_MatchesTheAnalyzerPackage()
    {
        var root = DiagnosticSnapshot.TryFindRepositoryRoot()!;
        var project = System.Xml.Linq.XDocument.Load(
            Path.Combine(
                root,
                "src",
                "HttpClient.Resilience.Analyzers.Package",
                "HttpClient.Resilience.Analyzers.Package.csproj"
            )
        );
        var version = Assert.Single(project.Descendants("Version")).Value;

        var (exitCode, output, error) = Run(["--version"]);

        Assert.Equal(ScanCommand.Success, exitCode);
        Assert.Equal($"hcr-scan {version}", output.Trim());
        Assert.Equal("", error);
    }

    [Fact]
    public void BadOption_ReturnsUsageError()
    {
        var (exitCode, output, error) = Run(["--bogus"]);

        Assert.Equal(ScanCommand.UsageError, exitCode);
        Assert.Contains("Unknown option '--bogus'.", error);
        Assert.Contains("Run 'hcr-scan --help' for usage.", error);
        Assert.Equal("", output);
    }

    [Fact]
    public void MissingTarget_ReturnsUsageError()
    {
        var (exitCode, _, error) = Run(["does-not-exist.slnx"]);

        Assert.Equal(ScanCommand.UsageError, exitCode);
        Assert.Contains("Nothing to scan at 'does-not-exist.slnx'", error);
    }

    [Fact]
    public void MissingAnalyzer_ReturnsScanFailed()
    {
        var directory = Directory.CreateTempSubdirectory("hcr-scan-command-").FullName;
        try
        {
            var (exitCode, _, error) = Run(
                [directory],
                Path.Combine(directory, "missing", "HttpClient.Resilience.Analyzers.dll")
            );

            Assert.Equal(ScanCommand.ScanFailed, exitCode);
            Assert.Contains("analyzer is missing", error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UnreadableBaseline_ReturnsUsageError()
    {
        var directory = Directory.CreateTempSubdirectory("hcr-scan-command-").FullName;
        try
        {
            var missing = Path.Combine(directory, "missing.sarif");
            var (exitCode, _, error) = Run(
                [directory, "--baseline", missing],
                typeof(ScanCommand).Assembly.Location
            );

            Assert.Equal(ScanCommand.UsageError, exitCode);
            Assert.Contains("Cannot read the baseline", error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(
        "/tmp/App/Client.cs(4,9): error HCR001: Do not create and dispose HttpClient per request",
        "The build failed because HCR001 is set to error severity, so projects that depend on a failing project were not scanned. Results below are partial. Set it to warning in .editorconfig to scan everything."
    )]
    [InlineData(
        "/tmp/App/Client.cs(4,9): error HCR060: Dispose the response\n/tmp/App/Client.cs(8,9): error HCR001: Do not create and dispose HttpClient",
        "The build failed because HCR001, HCR060 are set to error severity, so projects that depend on a failing project were not scanned. Results below are partial. Set them to warning in .editorconfig to scan everything."
    )]
    public void RuleErrorHint_ExplainsHowToScanEverything(string buildOutput, string expected)
    {
        Assert.Equal(expected, ScanCommand.RuleErrorHint(buildOutput));
    }

    [Fact]
    public void RuleErrorHint_ReturnsNullForOtherBuildFailures()
    {
        Assert.Null(ScanCommand.RuleErrorHint("error CS1002: ; expected"));
    }

    [Fact]
    public void BuildErrorExcerpt_ShowsOnlyErrors()
    {
        var output = """
            warning HCR001: ignored
            /tmp/App/Client.cs(1,1): error CS1002: ; expected
            /tmp/App/Client.cs(1,1): error CS1002: ; expected
            /tmp/App/Client.cs(2,1): error HCR001: finding
            """;

        var excerpt = ScanCommand.BuildErrorExcerpt(output);

        Assert.Equal(
            """
            dotnet build reported errors:
              /tmp/App/Client.cs(1,1): error CS1002: ; expected
              /tmp/App/Client.cs(2,1): error HCR001: finding
            """.ReplaceLineEndings("\n"),
            excerpt.ReplaceLineEndings("\n")
        );
    }

    [Fact]
    public void BuildArguments_InjectTargetsAndKeepFindingsFromStoppingTheBuild()
    {
        var options = new ScanOptions
        {
            Configuration = "Release",
            Framework = "net10.0",
            NoRestore = true,
            Verbose = true,
        };
        var targetsPath = Path.Combine(Path.GetTempPath(), "hcr", "scan.targets");

        var arguments = Scanner.BuildArguments(options, "/src/App.slnx", targetsPath);

        Assert.Equal("build", arguments[0]);
        Assert.Equal("/src/App.slnx", arguments[1]);
        Assert.Contains("--no-incremental", arguments);
        Assert.Contains("-p:CustomAfterMicrosoftCommonTargets=" + targetsPath, arguments);
        Assert.Contains("-p:RunAnalyzers=true", arguments);
        Assert.Contains("-p:EnableNETAnalyzers=false", arguments);
        Assert.Contains("-p:TreatWarningsAsErrors=false", arguments);
        Assert.Contains("-p:WarningsAsErrors=", arguments);
        Assert.Contains("--no-restore", arguments);
        Assert.Contains("-v:normal", arguments);
        Assert.DoesNotContain("-v:quiet", arguments);
        Assert.Contains("-c", arguments);
        Assert.Contains("Release", arguments);
        Assert.Contains("-f", arguments);
        Assert.Contains("net10.0", arguments);
    }

    [Fact]
    public void FormatArguments_SelectTheRequestedFixes()
    {
        var rules = ScanFixer.SelectRules(
            new ScanOptions { Rules = ["HCR001", "HCR002", "HCR062"], SkippedRules = ["HCR002"] }
        );

        Assert.Equal(["HCR001"], rules);

        var arguments = ScanFixer.FormatArguments(
            new ScanOptions { NoRestore = true },
            "/src/App.slnx",
            rules
        );

        Assert.Equal("format", arguments[0]);
        Assert.Equal("analyzers", arguments[1]);
        Assert.Equal("/src/App.slnx", arguments[2]);
        Assert.Contains("--severity", arguments);
        Assert.Contains("info", arguments);
        Assert.Contains("--diagnostics", arguments);
        Assert.Contains("HCR001", arguments);
        Assert.Contains("--no-restore", arguments);
    }
}
