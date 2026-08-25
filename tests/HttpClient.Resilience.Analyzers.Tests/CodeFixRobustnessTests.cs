using System.Collections.Concurrent;
using HttpClient.Resilience.Analyzers.CodeFixes;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;
using HttpClient.Resilience.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace HttpClient.Resilience.Analyzers.Tests;

/// <summary>
/// A code fix provider that throws surfaces as a broken lightbulb in the IDE. These tests run
/// every shipped provider over hostile source and require RegisterCodeFixesAsync to complete
/// without throwing, whatever the syntax looks like.
/// </summary>
public sealed class CodeFixRobustnessTests
{
    [Theory]
    [MemberData(nameof(ProviderTypeNames))]
    public async Task ProviderNeverThrowsOnHostileSource(string providerTypeName)
    {
        var failures = new ConcurrentBag<string>();

        foreach (var (name, source) in HostileSources)
        {
            if (!await RunAsync(providerTypeName, name, source, failures))
            {
                continue;
            }
        }

        Assert.True(
            failures.IsEmpty,
            $"{providerTypeName} threw while registering fixes:{System.Environment.NewLine}{string.Join(System.Environment.NewLine, failures)}");
    }

    public static TheoryData<string> ProviderTypeNames()
    {
        var data = new TheoryData<string>();
        foreach (var type in typeof(HCR060_DisposeResponseCodeFixProvider).Assembly.GetTypes())
        {
            if (typeof(CodeFixProvider).IsAssignableFrom(type) && !type.IsAbstract)
            {
                data.Add(type.FullName!);
            }
        }

        return data;
    }

    private static async Task<bool> RunAsync(
        string providerTypeName,
        string sourceName,
        string source,
        ConcurrentBag<string> failures)
    {
        var providerType = typeof(HCR060_DisposeResponseCodeFixProvider).Assembly.GetType(providerTypeName);
        if (providerType is null)
        {
            failures.Add($"provider type '{providerTypeName}' not found.");
            return false;
        }

        var provider = (CodeFixProvider)Activator.CreateInstance(providerType)!;

        try
        {
            var workspace = new AdhocWorkspace();
            var project = workspace.CurrentSolution
                .AddProject("CodeFixRobustness", "CodeFixRobustness", LanguageNames.CSharp)
                .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .AddMetadataReferences(TestCompilationFactory.References);
            var document = project.AddDocument(sourceName + ".cs", SourceText.From(source, System.Text.Encoding.UTF8));

            // Register against every diagnostic id the provider claims to fix so each
            // registration path executes, even where no real diagnostic exists.
            foreach (var diagnosticId in provider.FixableDiagnosticIds)
            {
                var descriptor = new DiagnosticDescriptor(
                    diagnosticId,
                    "robustness",
                    "robustness",
                    "robustness",
                    DiagnosticSeverity.Warning,
                    isEnabledByDefault: true);
                var context = new CodeFixContext(
                    document,
                    Diagnostic.Create(descriptor, Location.None),
                    (action, _) => { var ignored = action; },
                    CancellationToken.None);
                await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
            }

            return true;
        }
        catch (Exception exception)
        {
            failures.Add($"{sourceName}: {exception}");
            return false;
        }
    }

    private static IEnumerable<(string Name, string Source)> HostileSources
    {
        get
        {
            yield return ("empty", string.Empty);

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

            yield return ("conditional-access-everywhere", """
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
