using System.Text;
using System.Text.Json;
using HttpClient.Resilience.Analyzers.Scan;

namespace HttpClient.Resilience.Analyzers.Tests.Scan;

public sealed class ScanReportTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "hcr-scan-root");

    private static string InRoot(params string[] parts) => Path.Combine([Root, .. parts]);

    private static string FileUri(string path) => new Uri(path).AbsoluteUri;

    /// <summary>A SARIF 2.1 error log shaped like the one the C# compiler writes.</summary>
    private static string CompilerLog(params string[] results) =>
        $$"""
            {
              "$schema": "http://json.schemastore.org/sarif-2.1.0",
              "version": "2.1.0",
              "runs": [{
                "results": [{{string.Join(",", results)}}],
                "tool": { "driver": { "name": "Microsoft (R) Visual C# Compiler", "rules": [
                  { "id": "CA1822", "shortDescription": { "text": "Mark members as static" } },
                  { "id": "HCR001", "shortDescription": { "text": "Do not create and dispose HttpClient per request" },
                    "fullDescription": { "text": "Creating a client for each request can exhaust sockets." },
                    "properties": { "category": "Lifetime" },
                    "helpUri": "https://georgepwall1991.github.io/HttpClient.Resilience.Analyzers/rules/HCR001/" },
                  { "id": "HCR060", "shortDescription": { "text": "Dispose HttpResponseMessage when using ResponseHeadersRead" },
                    "defaultConfiguration": { "level": "note" },
                    "helpUri": "https://georgepwall1991.github.io/HttpClient.Resilience.Analyzers/rules/HCR060/" }
                ] } }
              }]
            }
            """;

    private static string Result(
        string ruleId,
        string level,
        string path,
        int line,
        int column,
        bool suppressed = false
    ) =>
        $$"""
            {
              "ruleId": "{{ruleId}}",
              "level": "{{level}}",
              "message": { "text": "{{ruleId}} message" },
              {{(suppressed ? "\"suppressions\": [{ \"kind\": \"inSource\" }]," : "")}}
              "locations": [{ "physicalLocation": {
                "artifactLocation": { "uri": "{{FileUri(path)}}" },
                "region": { "startLine": {{line}}, "startColumn": {{column}} } } }]
            }
            """;

    private static ScanReport ReadReport(params string[] logs)
    {
        var findings = new List<Finding>();
        var rules = new Dictionary<string, RuleInfo>(StringComparer.Ordinal);
        foreach (var log in logs)
            SarifReader.Read(log, findings, rules);
        return new ScanReport(Root, findings, rules);
    }

    [Fact]
    public void Reader_KeepsOnlyUnsuppressedHcrResults()
    {
        var report = ReadReport(
            CompilerLog(
                Result("HCR001", "warning", InRoot("src", "Client.cs"), 12, 9),
                Result("HCR001", "warning", InRoot("src", "Client.cs"), 40, 5, suppressed: true),
                Result("CA1822", "warning", InRoot("src", "Client.cs"), 3, 1),
                Result("HCR999", "warning", InRoot("src", "Client.cs"), 1, 1)
            )
        );

        var finding = Assert.Single(report.Findings);
        Assert.Equal(
            new Finding("HCR001", "Warning", "HCR001 message", "src/Client.cs", 12, 9),
            finding
        );
        Assert.Equal(["HCR001", "HCR060"], report.Rules.Keys.Order());
        Assert.Equal(
            "Do not create and dispose HttpClient per request",
            report.GetRule("HCR001").Title
        );
        Assert.Equal("Info", report.GetRule("HCR060").Severity);
        Assert.Equal("Warning", report.GetRule("HCR001").Severity);
    }

    [Fact]
    public void Reader_IgnoresLogsWithoutRunsOrLocations()
    {
        var findings = new List<Finding>();
        var rules = new Dictionary<string, RuleInfo>();

        SarifReader.Read("{}", findings, rules);
        SarifReader.Read(
            """{ "runs": [{ "results": [{ "ruleId": "HCR001", "level": "warning" }] }] }""",
            findings,
            rules
        );

        Assert.Empty(findings);
        Assert.Empty(rules);
    }

    [Fact]
    public void Findings_ReportedByEveryTargetFramework_AreCountedOnce()
    {
        var net8 = CompilerLog(Result("HCR001", "warning", InRoot("Client.cs"), 12, 9));
        var net10 = CompilerLog(
            Result("HCR001", "warning", InRoot("Client.cs"), 12, 9),
            Result("HCR001", "warning", InRoot("Client.cs"), 30, 9)
        );

        var report = ReadReport(net8, net10);

        Assert.Equal(2, report.Findings.Count);
        Assert.Equal(2, Assert.Single(report.RuleSummaries()).Count);
    }

    [Fact]
    public void RuleSummaries_PutMoreSevereRulesFirstThenMoreFrequent()
    {
        var report = ReadReport(
            CompilerLog(
                Result("HCR060", "note", InRoot("A.cs"), 1, 1),
                Result("HCR060", "note", InRoot("A.cs"), 2, 1),
                Result("HCR060", "note", InRoot("A.cs"), 3, 1),
                Result("HCR001", "warning", InRoot("B.cs"), 1, 1),
                Result("HCR001", "warning", InRoot("B.cs"), 2, 1),
                Result("HCR001", "warning", InRoot("B.cs"), 3, 1)
            )
        );

        Assert.Equal(
            ["HCR001", "HCR060"],
            report.RuleSummaries().Select(summary => summary.Rule.Id)
        );
        Assert.Equal(
            ["Warning", "Info"],
            report.RuleSummaries().Select(summary => summary.Severity)
        );
        Assert.Equal([("A.cs", 3), ("B.cs", 3)], report.TopFiles(2));
    }

    [Fact]
    public void Paths_OutsideTheRoot_StayAbsolute()
    {
        var outside = Path.Combine(Path.GetTempPath(), "elsewhere", "Shared.cs");

        Assert.Equal(PathUtil.Canonicalize(outside), ScanReport.ToRelativePath(Root, outside));
        Assert.Equal(
            "src/Deep/File.cs",
            ScanReport.ToRelativePath(Root, InRoot("src", "Deep", "File.cs"))
        );
    }

    [Fact]
    public void Text_ListsRulesFilesLinksAndTheSarifPath()
    {
        var report = ReadReport(
            CompilerLog(
                Result("HCR001", "warning", InRoot("src", "Client.cs"), 12, 9),
                Result("HCR001", "warning", InRoot("src", "Client.cs"), 20, 9),
                Result("HCR060", "note", InRoot("src", "Response.cs"), 5, 1)
            )
        );

        var text = report.RenderText(topFiles: 5, sarifPath: "httpclient-resilience.sarif");

        Assert.Contains("HCR Scan found 3 problems (2 rules, 2 files).", text);
        Assert.Contains(
            "  HCR001 Warning      2  Do not create and dispose HttpClient per request",
            text
        );
        Assert.Contains("  HCR060 Info         1  Dispose HttpResponseMessage", text);
        Assert.Contains("      2  src/Client.cs", text);
        Assert.Contains(
            "  HCR001  https://georgepwall1991.github.io/HttpClient.Resilience.Analyzers/rules/HCR001/",
            text
        );
        Assert.Contains("SARIF report: httpclient-resilience.sarif", text);
        Assert.Contains("dotnet add package HttpClient.Resilience.Analyzers", text);
        Assert.True(
            text.IndexOf("HCR001 Warning", StringComparison.Ordinal)
                < text.IndexOf("HCR060 Info", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Text_ListsEachRulesFindingsWithTheLineOfCode()
    {
        var root = Directory.CreateTempSubdirectory("hcr-scan-report-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src"));
            File.WriteAllLines(
                Path.Combine(root, "src", "Client.cs"),
                ["class Client", "{", "    HttpClient Create() => new HttpClient();", "}"]
            );
            var results = new[]
            {
                Result("HCR001", "warning", Path.Combine(root, "src", "Client.cs"), 3, 30),
                Result("HCR001", "warning", Path.Combine(root, "src", "Missing.cs"), 8, 5),
                Result("HCR001", "warning", Path.Combine(root, "src", "Client.cs"), 40, 1),
                Result("HCR060", "note", Path.Combine(root, "src", "Client.cs"), 1, 1),
            };
            var findings = new List<Finding>();
            var rules = new Dictionary<string, RuleInfo>(StringComparer.Ordinal);
            SarifReader.Read(CompilerLog(results), findings, rules);
            var report = new ScanReport(root, findings, rules);

            var text = report.RenderText(topFiles: 0, sarifPath: null, findingsPerRule: 2);

            Assert.Contains(
                """
                Findings:

                  HCR001  Do not create and dispose HttpClient per request
                    src/Client.cs:3:30
                      HCR001 message
                      3 | HttpClient Create() => new HttpClient();
                    src/Client.cs:40:1
                      HCR001 message
                    ...and 1 more in the SARIF report.

                  HCR060  Dispose HttpResponseMessage when using ResponseHeadersRead
                    src/Client.cs:1:1
                      HCR060 message
                      1 | class Client
                """.ReplaceLineEndings(),
                text
            );
            Assert.DoesNotContain(
                "Findings:",
                report.RenderText(topFiles: 0, sarifPath: null, findingsPerRule: 0)
            );
            Assert.DoesNotContain(
                "more in the SARIF report",
                report.RenderText(topFiles: 0, sarifPath: null, findingsPerRule: int.MaxValue)
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("**/Legacy/**", "src/Data/Legacy/2024_Init.cs", true)]
    [InlineData("Legacy", "src/Data/Legacy/2024_Init.cs", true)]
    [InlineData("Legacy", "src/Data/LegacyHelper.cs", false)]
    [InlineData("tests/**", "tests/App.Tests/ClientTests.cs", true)]
    [InlineData("tests/**", "src/tests.cs", false)]
    [InlineData("tests", "src/App/tests/Seed.cs", true)]
    [InlineData("*.Designer.cs", "src/Data/Model.Designer.cs", true)]
    [InlineData("src/*.cs", "src/Client.cs", true)]
    [InlineData("src/*.cs", "src/Client/Service.cs", false)]
    [InlineData("./src/Client?.cs", "src/Client2.cs", true)]
    [InlineData("src\\Legacy", "src/Legacy/Old.cs", true)]
    public void ExcludeGlobs_MatchRelativePaths(string glob, string path, bool expected)
    {
        Assert.Equal(expected, ScanFilter.GlobToRegex(glob).IsMatch(path));
    }

    [Fact]
    public void Filter_KeepsTheSelectedRulesAndFilesAndCountsTheRest()
    {
        var report = ReadReport(
            CompilerLog(
                Result("HCR001", "warning", InRoot("src", "Client.cs"), 12, 9),
                Result("HCR001", "warning", InRoot("src", "Legacy", "Old.cs"), 3, 1),
                Result("HCR060", "note", InRoot("src", "Client.cs"), 20, 1),
                Result("HCR088", "warning", InRoot("src", "Client.cs"), 30, 1)
            )
        );

        var filtered = report
            .Filter(new ScanFilter(["HCR001", "hcr060"], [], ["Legacy"]))
            .Filter(new ScanFilter([], ["HCR060"], []));

        var finding = Assert.Single(filtered.Findings);
        Assert.Equal(("HCR001", "src/Client.cs"), (finding.RuleId, finding.Path));
        Assert.Equal(3, filtered.FilteredOut);
        Assert.Contains(
            "3 more findings left out by --rules, --skip-rules or --exclude.",
            filtered.RenderText(topFiles: 0, sarifPath: null)
        );
        Assert.Same(report, report.Filter(new ScanFilter([], [], [])));
    }

    [Fact]
    public void CountAtLeast_CountsFindingsAtOrAboveASeverity()
    {
        var report = ReadReport(
            CompilerLog(
                Result("HCR001", "error", InRoot("A.cs"), 1, 1),
                Result("HCR001", "warning", InRoot("A.cs"), 2, 1),
                Result("HCR060", "note", InRoot("A.cs"), 3, 1)
            )
        );

        Assert.Equal(1, report.CountAtLeast("Error"));
        Assert.Equal(2, report.CountAtLeast("Warning"));
        Assert.Equal(3, report.CountAtLeast("Info"));
    }

    [Fact]
    public void Baseline_MatchesFingerprintsAndMarksKnownFindings()
    {
        var root = Directory.CreateTempSubdirectory("hcr-scan-baseline-").FullName;
        try
        {
            File.WriteAllText(
                Path.Combine(root, "Client.cs"),
                """
                class Client
                {
                    HttpClient Create() => new HttpClient();
                }
                """
            );
            var report = new ScanReport(
                root,
                [
                    new Finding(
                        "HCR001",
                        "Warning",
                        "HCR001 message",
                        Path.Combine(root, "Client.cs"),
                        3,
                        30
                    ),
                ],
                new Dictionary<string, RuleInfo>
                {
                    ["HCR001"] = new(
                        "HCR001",
                        "Do not create and dispose HttpClient per request",
                        "Warning",
                        "https://example.test/HCR001"
                    ),
                }
            );

            using var baselineStream = new MemoryStream();
            report.WriteSarif(baselineStream, "1.2.3");
            var baseline = ScanBaseline.Parse(Encoding.UTF8.GetString(baselineStream.ToArray()));
            var filtered = report.ApplyBaseline(baseline);

            Assert.Empty(filtered.Findings);
            Assert.Single(filtered.BaselineFindings);
            Assert.True(filtered.HasBaseline);
            Assert.Contains(
                "1 finding already in the baseline is not listed",
                filtered.RenderText(0, null)
            );

            using var sarifStream = new MemoryStream();
            filtered.WriteSarif(sarifStream, "1.2.3");
            using var document = JsonDocument.Parse(sarifStream.ToArray());
            var result = Assert.Single(
                document.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
            );
            Assert.Equal("unchanged", result.GetProperty("baselineState").GetString());
            Assert.Equal(
                report.Fingerprint(report.Findings[0]),
                result
                    .GetProperty("partialFingerprints")
                    .GetProperty(ScanReport.FingerprintKey)
                    .GetString()
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Sarif_ContainsRelativeLocationsAndRuleMetadata()
    {
        var report = ReadReport(
            CompilerLog(Result("HCR001", "warning", InRoot("src", "Client.cs"), 12, 9))
        );

        using var stream = new MemoryStream();
        report.WriteSarif(stream, "1.2.3");
        using var document = JsonDocument.Parse(stream.ToArray());
        var run = document.RootElement.GetProperty("runs")[0];
        var rule = Assert.Single(
            run.GetProperty("tool").GetProperty("driver").GetProperty("rules").EnumerateArray()
        );
        var result = Assert.Single(run.GetProperty("results").EnumerateArray());

        Assert.Equal("HCR001", rule.GetProperty("id").GetString());
        Assert.Contains(
            "httpclient",
            rule.GetProperty("properties")
                .GetProperty("tags")
                .EnumerateArray()
                .Select(value => value.GetString())
        );
        Assert.Equal(
            "src/Client.cs",
            result
                .GetProperty("locations")[0]
                .GetProperty("physicalLocation")
                .GetProperty("artifactLocation")
                .GetProperty("uri")
                .GetString()
        );
        Assert.Equal("warning", result.GetProperty("level").GetString());
    }

    [Fact]
    public void Html_ShowsTheFindingAndNearbySource()
    {
        var root = Directory.CreateTempSubdirectory("hcr-scan-html-").FullName;
        try
        {
            File.WriteAllText(
                Path.Combine(root, "Client.cs"),
                """
                class Client
                {
                    HttpClient Create() => new HttpClient();
                }
                """
            );
            var report = new ScanReport(
                root,
                [
                    new Finding(
                        "HCR001",
                        "Warning",
                        "HCR001 message",
                        Path.Combine(root, "Client.cs"),
                        3,
                        30
                    ),
                ],
                new Dictionary<string, RuleInfo>
                {
                    ["HCR001"] = new(
                        "HCR001",
                        "Do not create and dispose HttpClient per request",
                        "Warning",
                        "https://example.test/HCR001"
                    ),
                }
            );

            var html = HtmlReport.Render(report, "1.2.3", root, DateTimeOffset.UnixEpoch);

            Assert.Contains("HCR Scan report", html);
            Assert.Contains("HCR001", html);
            Assert.Contains("HttpClient Create()", html);
            Assert.Contains("class=\"line hit\"", html);
            Assert.Contains("scanning/", html);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
