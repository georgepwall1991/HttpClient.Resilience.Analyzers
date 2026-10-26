using System.Reflection;
using System.Xml.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Scan;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;
using Microsoft.CodeAnalysis.CodeFixes;

namespace HttpClient.Resilience.Analyzers.Tests.Scan;

public sealed class ScanMetadataTests
{
    private static string RepositoryRoot =>
        DiagnosticSnapshot.TryFindRepositoryRoot()
        ?? throw new InvalidOperationException("Could not find the repository root.");

    [Fact]
    public void ScannerReportsEveryShippedRule()
    {
        var shipped = typeof(DiagnosticIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(shipped, ScanTargets.ReportedRules.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ScannerFixableRulesMatchTheCodeFixProviders()
    {
        var fixable = typeof(DiagnosticIds)
            .Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(CodeFixProvider).IsAssignableFrom(type))
            .Select(type => (CodeFixProvider)Activator.CreateInstance(type)!)
            .SelectMany(provider => provider.FixableDiagnosticIds)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(fixable, ScanFixer.FixableRules.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ScannerAndAnalyzerPackagesShareTheSameVersion()
    {
        var analyzerVersion = VersionOf(
            Path.Combine(
                RepositoryRoot,
                "src",
                "HttpClient.Resilience.Analyzers.Package",
                "HttpClient.Resilience.Analyzers.Package.csproj"
            )
        );
        Assert.Equal(analyzerVersion, ScanCommand.ToolVersion);
    }

    [Fact]
    public void ScannerPackagesAsTheHcrTool()
    {
        var project = XDocument.Load(
            Path.Combine(
                RepositoryRoot,
                "src",
                "HttpClient.Resilience.Analyzers.Scan",
                "HttpClient.Resilience.Analyzers.Scan.csproj"
            )
        );

        Assert.Equal(
            "HttpClient.Resilience.Analyzers.Scan",
            Assert.Single(project.Descendants("PackageId")).Value
        );
        Assert.Equal("hcr-scan", Assert.Single(project.Descendants("ToolCommandName")).Value);
        Assert.Equal("true", Assert.Single(project.Descendants("PackAsTool")).Value);
        Assert.Equal("README.md", Assert.Single(project.Descendants("PackageReadmeFile")).Value);
    }

    [Fact]
    public void GlobalConfigCoversEveryReportedRule()
    {
        var globalConfig = ScanTargets.GlobalConfig();

        foreach (var rule in ScanTargets.ReportedRules)
            Assert.Contains($"dotnet_diagnostic.{rule}.severity = default", globalConfig);
        Assert.Contains("is_global = true", globalConfig);
        Assert.Contains("global_level = -1000", globalConfig);
    }

    [Fact]
    public void ScanTargets_EscapePathsAndInjectTheScannerAnalyzer()
    {
        var root = Path.Combine(Path.GetTempPath(), "hcr & scan");
        var targets = ScanTargets.Create(
            Path.Combine(root, "analyzer", "HttpClient.Resilience.Analyzers.dll"),
            Path.Combine(root, "logs"),
            Path.Combine(root, ScanTargets.GlobalConfigFileName)
        );

        Assert.Contains("_HcrScanInject", targets);
        Assert.Contains("'%(Filename)' == 'HttpClient.Resilience.Analyzers'", targets);
        Assert.Contains("hcr &amp; scan", targets);
        Assert.Contains("version=2.1", targets);
        Assert.Contains("HttpClient.Resilience.Analyzers.Scan.globalconfig", targets);
    }

    private static string VersionOf(string projectPath)
    {
        var project = XDocument.Load(projectPath);
        return Assert.Single(project.Descendants("Version")).Value;
    }
}
