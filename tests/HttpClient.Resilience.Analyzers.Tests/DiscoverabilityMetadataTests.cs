using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HttpClient.Resilience.Analyzers.Tests;

/// <summary>
/// Guards NuGet/GitHub discoverability assets: package description/tags, README funnel,
/// and product-flow visuals that ship with PackageReadmeFile.
/// </summary>
public sealed class DiscoverabilityMetadataTests
{
    private static string RepositoryRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void Analyzer_package_description_and_tags_include_high_intent_httpclient_terms()
    {
        var csproj = XDocument.Load(
            Path.Combine(
                RepositoryRoot,
                "src",
                "HttpClient.Resilience.Analyzers.Package",
                "HttpClient.Resilience.Analyzers.Package.csproj"));

        var description = Assert.Single(csproj.Descendants("Description")).Value;
        var tags = Assert.Single(csproj.Descendants("PackageTags")).Value;
        var title = Assert.Single(csproj.Descendants("Title")).Value;
        var readmeFile = Assert.Single(csproj.Descendants("PackageReadmeFile")).Value;
        var version = Assert.Single(csproj.Descendants("Version")).Value;
        var projectUrl = Assert.Single(csproj.Descendants("PackageProjectUrl")).Value;

        Assert.Equal("README.md", readmeFile);
        Assert.Equal(
            "https://georgepwall1991.github.io/HttpClient.Resilience.Analyzers/",
            projectUrl);
        Assert.Contains("IHttpClientFactory", title, StringComparison.Ordinal);
        Assert.Contains("Polly", title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Http.Resilience", title, StringComparison.Ordinal);

        foreach (
            var term in new[]
            {
                "Compile-time",
                "HttpClient",
                "IHttpClientFactory",
                "AddHttpClient",
                "PooledConnectionLifetime",
                "AddStandardResilienceHandler",
                "Polly",
                "Microsoft.Extensions.Http.Resilience",
                "Roslyn",
            })
        {
            Assert.True(
                description.Contains(term, StringComparison.Ordinal),
                $"Analyzer Description must contain '{term}' for NuGet search discoverability.");
        }

        foreach (
            var tag in new[]
            {
                "httpclient",
                "ihttpclientfactory",
                "AddHttpClient",
                "PooledConnectionLifetime",
                "AddStandardResilienceHandler",
                "polly",
                "roslyn-analyzer",
                "analyzers",
                "socket-exhaustion",
                "typed-client",
                "microsoft-extensions-http-resilience",
                "AddStandardHedgingHandler",
                "hedging",
            })
        {
            Assert.True(
                tags.Contains(tag, StringComparison.Ordinal),
                $"Analyzer PackageTags must include '{tag}'.");
        }

        Assert.Matches(@"^\d+\.\d+\.\d+$", version);
    }

    [Fact]
    public void Readme_conversion_funnel_and_product_visuals_exist_with_resolvable_paths()
    {
        var version = Assert
            .Single(
                XDocument
                    .Load(
                        Path.Combine(
                            RepositoryRoot,
                            "src",
                            "HttpClient.Resilience.Analyzers.Package",
                            "HttpClient.Resilience.Analyzers.Package.csproj"))
                    .Descendants("Version"))
            .Value;

        foreach (var readmeName in new[] { "README.md", "PACKAGE_README.md" })
        {
            var readmePath = Path.Combine(RepositoryRoot, readmeName);
            var readme = File.ReadAllText(readmePath);

            foreach (
                var section in new[]
                {
                    "## The problem",
                    "## What it catches",
                    "## Install",
                    "## See it work",
                    "## Quick start",
                    "## Feature snapshot",
                })
            {
                Assert.True(
                    readme.Contains(section, StringComparison.Ordinal),
                    $"{readmeName} must contain funnel section '{section}'.");
            }

            Assert.Contains("PrivateAssets=\"all\"", readme, StringComparison.Ordinal);
            Assert.Contains($"Version=\"{version}\"", readme, StringComparison.Ordinal);
            Assert.Contains("HCR001", readme, StringComparison.Ordinal);
            Assert.Contains("HCR041", readme, StringComparison.Ordinal);
            Assert.Contains("HCR085", readme, StringComparison.Ordinal);

            // NuGet.org requires absolute HTTPS image URLs in PackageReadmeFile content.
            const string rawBase =
                "https://raw.githubusercontent.com/georgepwall1991/HttpClient.Resilience.Analyzers/main/";

            var visualAssets = new[]
            {
                "assets/flow-ide-diagnostics.svg",
                "assets/flow-before-after-fix.svg",
                "assets/flow-product-loop.svg",
            };

            foreach (var asset in visualAssets)
            {
                Assert.Contains(rawBase + asset, readme, StringComparison.Ordinal);
            }

            Assert.Contains(rawBase + "assets/logo.png", readme, StringComparison.Ordinal);

            var imageRefs = Regex
                .Matches(readme, @"!\[[^\]]*\]\(([^)]+)\)")
                .Select(m => m.Groups[1].Value)
                .Concat(
                    Regex
                        .Matches(readme, @"<img[^>]+src=""([^""]+)""")
                        .Select(m => m.Groups[1].Value))
                .Distinct(StringComparer.Ordinal);

            foreach (var imageRef in imageRefs)
            {
                Assert.True(
                    imageRef.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
                    $"{readmeName} image must use absolute HTTPS for NuGet rendering: {imageRef}");
            }
        }

        // Root README keeps the full rule encyclopedia below the funnel.
        var rootReadme = File.ReadAllText(Path.Combine(RepositoryRoot, "README.md"));
        Assert.Contains("## Rule Catalog", rootReadme, StringComparison.Ordinal);
        Assert.Contains("stays quiet", rootReadme, StringComparison.OrdinalIgnoreCase);

        foreach (
            var asset in new[]
            {
                "assets/flow-ide-diagnostics.svg",
                "assets/flow-before-after-fix.svg",
                "assets/flow-product-loop.svg",
                "assets/logo.png",
                "assets/icon.png",
            })
        {
            var fullPath = Path.Combine(RepositoryRoot, asset);
            Assert.True(File.Exists(fullPath), $"Missing README visual: {asset}");
            Assert.True(new FileInfo(fullPath).Length > 0, $"Empty README visual: {asset}");
        }
    }

    [Fact]
    public void Analyzer_package_packs_all_assets_for_nuget_readme_rendering()
    {
        var package = XDocument.Load(
            Path.Combine(
                RepositoryRoot,
                "src",
                "HttpClient.Resilience.Analyzers.Package",
                "HttpClient.Resilience.Analyzers.Package.csproj"));

        Assert.Contains(
            package.Descendants("None"),
            n =>
                (n.Attribute("Include")?.Value ?? string.Empty).Contains(
                    "assets",
                    StringComparison.Ordinal)
                && string.Equals(
                    n.Attribute("Pack")?.Value,
                    "true",
                    StringComparison.OrdinalIgnoreCase)
                && (n.Attribute("PackagePath")?.Value ?? string.Empty).Contains(
                    "assets",
                    StringComparison.Ordinal));
    }
}
