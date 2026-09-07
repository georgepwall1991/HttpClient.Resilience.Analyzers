using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Composition;
using System.Reflection;
using HttpClient.Resilience.Analyzers.CodeFixes;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace HttpClient.Resilience.Analyzers.Tests;

/// <summary>
/// A code fix provider that throws surfaces as a broken lightbulb in the IDE. These tests run
/// every MEF-exported provider over real analyzer diagnostics anchored in hostile source and
/// require RegisterCodeFixesAsync to complete without throwing.
/// </summary>
public sealed class CodeFixRobustnessTests
{
    private static readonly string[] ExpectedProviderFullNames =
    {
        typeof(HCR001_UseHttpClientFactoryCodeFixProvider).FullName!,
        typeof(HCR002_AddPooledConnectionLifetimeCodeFixProvider).FullName!,
        typeof(HCR004_ChangeToScopedLifetimeCodeFixProvider).FullName!,
        typeof(HCR005_RemoveDuplicateTypedClientRegistrationCodeFixProvider).FullName!,
        typeof(HCR040_RemoveDuplicateStandardResilienceHandlerCodeFixProvider).FullName!,
        typeof(HCR041_DisableUnsafeMethodRetriesCodeFixProvider).FullName!,
        typeof(HCR042_ReplaceHedgingWithSafeResilienceCodeFixProvider).FullName!,
        typeof(HCR043_DisableUnsafeMethodRetriesCodeFixProvider).FullName!,
        typeof(HCR060_DisposeResponseCodeFixProvider).FullName!,
        typeof(HCR061_EnsureSuccessStatusCodeCodeFixProvider).FullName!,
        typeof(HCR063_AwaitHttpOperationCodeFixProvider).FullName!,
        typeof(HCR064_PassCancellationTokenCodeFixProvider).FullName!,
        typeof(HCR081_DisposeStreamCodeFixProvider).FullName!,
        typeof(HCR085_AddExplicitClientNameCodeFixProvider).FullName!
    };

    [Fact]
    public void EveryShippedProviderIsMefExportedWithExpectedCount()
    {
        var exported = typeof(HCR060_DisposeResponseCodeFixProvider).Assembly
            .GetTypes()
            .Where(type => typeof(CodeFixProvider).IsAssignableFrom(type) && !type.IsAbstract)
            .Where(type => type.GetCustomAttribute<ExportAttribute>() is { ContractName: LanguageNames.CSharp } ||
                type.GetCustomAttributes<ExportCodeFixProviderAttribute>().Any())
            .Select(type => type.FullName!)
            .OrderBy(fullName => fullName)
            .ToArray();

        var expected = ExpectedProviderFullNames.OrderBy(fullName => fullName).ToArray();
        Assert.Equal(expected, exported);
    }

    [Theory]
    [MemberData(nameof(HostileSourceNames))]
    public async Task EveryProviderSurvivesRegisteringAgainstRealDiagnostics(string sourceName)
    {
        var failures = new ConcurrentBag<string>();

        foreach (var providerTypeName in ExpectedProviderFullNames)
        {
            await RunAsync(providerTypeName, sourceName, failures);
        }

        Assert.True(
            failures.IsEmpty,
            $"Failures:{System.Environment.NewLine}{string.Join(System.Environment.NewLine, failures)}");
    }

    [Fact]
    public async Task PositiveControl_ProviderRegistersActionOnRealDiagnostic()
    {
        const string source = """
            using System.Net.Http;

            public sealed class PaymentsSingleton
            {
                private static readonly HttpClient Primary = new HttpClient();
            }
            """;

        var registered = await RegisterAllFixesForMatchingProvidersAsync(source);
        Assert.True(registered > 0, "positive control registered no actions.");
    }

    private static async Task<int> RegisterAllFixesForMatchingProvidersAsync(string source)
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.CurrentSolution
            .AddProject("CodeFixRobustness", "CodeFixRobustness", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(TestCompilationFactory.References);
        var document = project.AddDocument("Test.cs", SourceText.From(source, System.Text.Encoding.UTF8));

        var compilation = await document.Project.GetCompilationAsync().ConfigureAwait(false);
        Assert.NotNull(compilation);

        var analyzerDiagnostics = await compilation!
            .WithAnalyzers(AnalyzerCatalog.CreateAll())
            .GetAnalyzerDiagnosticsAsync();

        var providers = ExpectedProviderFullNames
            .Select(fullName => (CodeFixProvider)Activator.CreateInstance(
                typeof(HCR060_DisposeResponseCodeFixProvider).Assembly.GetType(fullName)!)!)
            .ToArray();

        var registered = 0;
        foreach (var diagnostic in analyzerDiagnostics)
        {
            foreach (var provider in providers)
            {
                if (!provider.FixableDiagnosticIds.Contains(diagnostic.Id))
                {
                    continue;
                }

                var actions = new List<CodeAction>();
                var context = new CodeFixContext(document, diagnostic,
                    (action, _) => actions.Add(action), CancellationToken.None);
                await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
                registered += actions.Count;
            }
        }

        return registered;
    }

    private static async Task RunAsync(string providerTypeName, string sourceName, ConcurrentBag<string> failures)
    {
        var providerType = typeof(HCR060_DisposeResponseCodeFixProvider).Assembly.GetType(providerTypeName);
        if (providerType is null)
        {
            failures.Add($"{sourceName}: provider '{providerTypeName}' not found.");
            return;
        }

        var provider = (CodeFixProvider)Activator.CreateInstance(providerType)!;
        var workspace = new AdhocWorkspace();
        var project = workspace.CurrentSolution
            .AddProject(sourceName, sourceName, LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(TestCompilationFactory.References);

        // Hostile sources intentionally contain compiler errors, so parse-only documents are
        // used when compilation is impossible.
        Document? document = null;
        try
        {
            document = project.AddDocument(sourceName + ".cs", SourceText.From(sourceNameSource(sourceName), System.Text.Encoding.UTF8));
        }
        catch (Exception exception)
        {
            failures.Add($"{sourceName}: adding document threw {exception}");
            return;
        }

        var compilation = await document.Project.GetCompilationAsync();
        ImmutableArray<Diagnostic> diagnostics;
        if (compilation is null)
        {
            diagnostics = ImmutableArray<Diagnostic>.Empty;
        }
        else
        {
            diagnostics = await compilation
                .WithAnalyzers(AnalyzerCatalog.CreateAll())
                .GetAnalyzerDiagnosticsAsync();
        }

        foreach (var diagnostic in diagnostics)
        {
            foreach (var fixableId in provider.FixableDiagnosticIds)
            {
                if (fixableId != diagnostic.Id)
                {
                    continue;
                }

                try
                {
                    var context = new CodeFixContext(document, diagnostic,
                        (action, _) => { var ignored = action; }, CancellationToken.None);
                    await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures.Add($"{sourceName} [{diagnostic.Id}]: {exception}");
                }
            }
        }
    }

    private static string sourceNameSource(string sourceName)
    {
        return HostileSources.First(candidate => candidate.Name == sourceName).Source;
    }

    public static TheoryData<string> HostileSourceNames()
    {
        var data = new TheoryData<string>();
        foreach (var candidate in HostileSources)
        {
            data.Add(candidate.Name);
        }

        return data;
    }

    private static IEnumerable<(string Name, string Source)> HostileSources
    {
        get
        {
            yield return ("unresolved-types", """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddHttpClient<UnknownClient>().AddStandardResilienceHandler();
                        services.AddHttpClient<A, B>().AddStandardHedgingHandler();
                        services.AddSingleton<UnknownJob>();
                        services.AddSingleton(typeof(UnknownOther));
                        services.AddResilienceHandler("x", builder => builder.AddRetry(new UnknownOptions()));
                    }
                }
                """);

            yield return ("generic-arity-mismatch", """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddHttpClient<A, B, C>().AddStandardResilienceHandler(1, 2, 3);
                        services.AddSingleton<>();
                        services.AddHttpClient<IPaymentsClient, Payments.StripePaymentsClient>();
                    }
                }
                """);

            yield return ("lambda-with-wrong-arity", """
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddStandardResilienceHandler((a, b) => a.Retry.ShouldHandle = null);
                        services.AddStandardResilienceHandler(options => options.Missing.Chain[0] = 1);
                        services.AddStandardResilienceHandler(options => options.Retry.ShouldHandle = null);
                    }
                }
                """);

            yield return ("unsafe-methods-everywhere", """
                public sealed class Jobs(IHttpClientFactory factory)
                {
                    public System.Threading.Tasks.Task A() => factory.CreateClient("a").PostAsync("/x", null);
                    public System.Threading.Tasks.Task B() => factory.CreateClient("b").DeleteAsync("/y");

                    public static void Configure(IServiceCollection services)
                    {
                        services.AddHttpClient("a").AddStandardHedgingHandler();
                        services.AddHttpClient("b").AddStandardResilienceHandler();
                    }
                }

                public interface IHttpClientFactory
                {
                    System.Threading.Tasks.Task PostAsync(string url, object? content);
                    System.Threading.Tasks.Task DeleteAsync(string url);
                }
                """);

            yield return ("conditional-access-and-streams", """
                public sealed class Client
                {
                    public void Do(HttpClient? client)
                    {
                        client?.DefaultRequestHeaders?.Add("x", "y");
                        using var response = client?.SendAsync(default!).Result;
                        response?.EnsureSuccessStatusCode();
                        var stream = response?.Content?.ReadAsStream();
                        stream?.WriteByte(1);
                    }
                }
                """);

            yield return ("deeply-nested-using", """
                public sealed class Client
                {
                    public void Do()
                    {
                        if (true)
                            if (true)
                                if (true)
                                    using (new System.IO.MemoryStream())
                                    {
                                        var stream = new System.IO.MemoryStream();
                                        stream.WriteByte(1);
                                    }
                    }
                }
                """);

            yield return ("await-using-and-tuples", """
                public sealed class Client
                {
                    public async System.Threading.Tasks.Task Do(System.Net.Http.HttpClient client)
                    {
                        await using var s = new System.IO.MemoryStream();
                        var (a, b) = (client, s);
                        _ = a; _ = b;
                        var tuple = (Response: await client.GetAsync("https://example.com"), Other: 1);
                        _ = tuple.Response;
                    }
                }
                """);
        }
    }
}
