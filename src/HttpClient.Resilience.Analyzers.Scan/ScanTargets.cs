using System.Security;

namespace HttpClient.Resilience.Analyzers.Scan;

/// <summary>
/// The MSBuild file the scan injects through <c>CustomAfterMicrosoftCommonTargets</c>. It swaps any
/// HttpClient.Resilience.Analyzers analyzer the project already references for the scanner's copy (so rules are not
/// reported twice or by an older version) and gives every compilation its own SARIF error log. One shared
/// <c>ErrorLog</c> path would be overwritten by each compilation, and even a name per project and target
/// framework collides when a solution builds one project twice with different global properties.
/// It also hands the compiler <see cref="GlobalConfig"/>, which keeps severities a repository sets for all
/// analyzers or a whole category from turning the findings into build errors.
/// </summary>
internal static class ScanTargets
{
    public const string TargetsFileName = "HttpClient.Resilience.Analyzers.Scan.targets";
    public const string GlobalConfigFileName = "HttpClient.Resilience.Analyzers.Scan.globalconfig";

    /// <summary>
    /// Every shipped HCR rule, in catalog order. A test keeps this list equal to the analyzer's
    /// <c>DiagnosticIds</c> so a new rule cannot be added without the scanner injecting it.
    /// </summary>
    public static readonly IReadOnlyList<string> ReportedRules =
    [
        "HCR001",
        "HCR002",
        "HCR003",
        "HCR004",
        "HCR005",
        "HCR006",
        "HCR020",
        "HCR021",
        "HCR022",
        "HCR040",
        "HCR041",
        "HCR042",
        "HCR043",
        "HCR060",
        "HCR061",
        "HCR062",
        "HCR063",
        "HCR064",
        "HCR065",
        "HCR080",
        "HCR081",
        "HCR082",
        "HCR083",
        "HCR084",
        "HCR085",
        "HCR087",
        "HCR088",
    ];

    /// <summary>
    /// Writes the targets file and the global config it passes to the compiler into <paramref name="workDirectory"/>
    /// and returns the targets file's path.
    /// </summary>
    public static string Write(
        string workDirectory,
        string analyzerAssemblyPath,
        string errorLogDirectory
    )
    {
        var globalConfigPath = Path.Combine(workDirectory, GlobalConfigFileName);
        File.WriteAllText(globalConfigPath, GlobalConfig());

        var targetsPath = Path.Combine(workDirectory, TargetsFileName);
        File.WriteAllText(
            targetsPath,
            Create(analyzerAssemblyPath, errorLogDirectory, globalConfigPath)
        );
        return targetsPath;
    }

    public static string Create(
        string analyzerAssemblyPath,
        string errorLogDirectory,
        string globalConfigPath
    )
    {
        var analyzer = SecurityElement.Escape(Path.GetFullPath(analyzerAssemblyPath));
        var logDirectory = SecurityElement.Escape(Path.GetFullPath(errorLogDirectory));
        var globalConfig = SecurityElement.Escape(Path.GetFullPath(globalConfigPath));

        return $"""
            <Project>
              <!-- Written by hcr-scan for one scan and deleted afterwards. -->
              <Target Name="_HcrScanInject" BeforeTargets="CoreCompile" Condition="'$(Language)' == 'C#'">
                <ItemGroup>
                  <Analyzer Remove="@(Analyzer)" Condition="'%(Filename)' == 'HttpClient.Resilience.Analyzers'" />
                  <Analyzer Include="{analyzer}" />
                  <EditorConfigFiles Include="{globalConfig}" />
                </ItemGroup>
                <PropertyGroup>
                  <ErrorLog>{logDirectory}{Path.DirectorySeparatorChar}$(MSBuildProjectName).$([System.Guid]::NewGuid().ToString('N')).sarif,version=2.1</ErrorLog>
                </PropertyGroup>
              </Target>
            </Project>
            """;
    }

    /// <summary>
    /// A global analyzer config that sets each reported rule to its <c>default</c> severity. A setting for one rule
    /// id wins over <c>dotnet_analyzer_diagnostic.severity</c> and <c>dotnet_analyzer_diagnostic.category-*.severity</c>,
    /// so a repository that raises every analyzer diagnostic to <c>error</c> does not fail the scan build on the
    /// findings, and the projects that depend on it are still built and scanned. The repository's own settings for a
    /// rule id still win: an <c>.editorconfig</c> entry always beats a global config, and the lowest global level
    /// loses to any global config the repository has.
    /// </summary>
    public static string GlobalConfig()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("# Written by hcr-scan for one scan and deleted afterwards.");
        text.AppendLine("is_global = true");
        text.AppendLine("global_level = -1000");
        foreach (var rule in ReportedRules)
            text.AppendLine($"dotnet_diagnostic.{rule}.severity = default");
        return text.ToString();
    }
}
