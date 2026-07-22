using HttpClient.Resilience.Analyzers.Analyzers.Resilience;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;

namespace HttpClient.Resilience.Analyzers.Tests.Resilience;

public sealed class HCR082_PerRequestResiliencePipelineAnalyzerTests
{
    [Fact]
    public async Task ReportsDiagnostic_WhenControllerBuildsPipelinePerRequest()
    {
        const string source = """
            public sealed class PaymentsController
            {
                public void Post()
                {
                    var pipeline = new Polly.ResiliencePipelineBuilder().Build();
                }
            }

            namespace Polly
            {
                public sealed class ResiliencePipeline
                {
                }

                public sealed class ResiliencePipelineBuilder
                {
                    public ResiliencePipeline Build()
                    {
                        return new ResiliencePipeline();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR082_PerRequestResiliencePipelineAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR082, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenPipelineBuilderChainBuildsInRequestPath()
    {
        const string source = """
            using Polly;

            public sealed class PaymentsService
            {
                public void Send()
                {
                    var pipeline = new ResiliencePipelineBuilder()
                        .AddRetry()
                        .Build();
                }
            }

            namespace Polly
            {
                public sealed class ResiliencePipeline
                {
                }

                public sealed class ResiliencePipelineBuilder
                {
                    public ResiliencePipelineBuilder AddRetry()
                    {
                        return this;
                    }

                    public ResiliencePipeline Build()
                    {
                        return new ResiliencePipeline();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR082_PerRequestResiliencePipelineAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR082, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenVisibleBuilderLocalBuildsInRequestPath()
    {
        const string source = """
            using Polly;

            public sealed class PaymentsEndpoint
            {
                public void Handle()
                {
                    var builder = new ResiliencePipelineBuilder();
                    var pipeline = builder.Build();
                }
            }

            namespace Polly
            {
                public sealed class ResiliencePipeline
                {
                }

                public sealed class ResiliencePipelineBuilder
                {
                    public ResiliencePipeline Build()
                    {
                        return new ResiliencePipeline();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR082_PerRequestResiliencePipelineAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR082, diagnostic.Id);
    }

    [Fact]
    public async Task ReportsDiagnostic_WhenMinimalApiEndpointBuildsPipeline()
    {
        const string source = """
            using System;
            using Polly;

            var app = new WebApplication();
            app.MapPost("/payments", () =>
            {
                var pipeline = new ResiliencePipelineBuilder().Build();
            });

            public sealed class WebApplication
            {
                public void MapPost(string pattern, Action handler)
                {
                }
            }

            namespace Polly
            {
                public sealed class ResiliencePipeline
                {
                }

                public sealed class ResiliencePipelineBuilder
                {
                    public ResiliencePipeline Build()
                    {
                        return new ResiliencePipeline();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR082_PerRequestResiliencePipelineAnalyzer>.GetDiagnosticsAsync(source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.HCR082, diagnostic.Id);
    }

    [Fact]
    public async Task DoesNotReport_WhenStartupBuildsPipeline()
    {
        const string source = """
            using Polly;

            public sealed class Startup
            {
                public void Configure()
                {
                    var pipeline = new ResiliencePipelineBuilder().Build();
                }
            }

            namespace Polly
            {
                public sealed class ResiliencePipeline
                {
                }

                public sealed class ResiliencePipelineBuilder
                {
                    public ResiliencePipeline Build()
                    {
                        return new ResiliencePipeline();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR082_PerRequestResiliencePipelineAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenPipelineIsStaticField()
    {
        const string source = """
            using Polly;

            public sealed class PaymentsService
            {
                private static readonly ResiliencePipeline Pipeline = new ResiliencePipelineBuilder().Build();
            }

            namespace Polly
            {
                public sealed class ResiliencePipeline
                {
                }

                public sealed class ResiliencePipelineBuilder
                {
                    public ResiliencePipeline Build()
                    {
                        return new ResiliencePipeline();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR082_PerRequestResiliencePipelineAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenResolvedCustomBuilderBuildsInRequestPath()
    {
        const string source = """
            public sealed class PaymentsController
            {
                public void Post()
                {
                    var pipeline = new Custom.ResiliencePipelineBuilder().Build();
                }
            }

            namespace Custom
            {
                public sealed class ResiliencePipeline
                {
                }

                public sealed class ResiliencePipelineBuilder
                {
                    public ResiliencePipeline Build()
                    {
                        return new ResiliencePipeline();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR082_PerRequestResiliencePipelineAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_WhenCustomExtensionBuildsFromPollyBuilder()
    {
        const string source = """
            using Custom;
            using Polly;

            public sealed class PaymentsController
            {
                public void Post()
                {
                    var pipeline = new ResiliencePipelineBuilder().Build();
                }
            }

            namespace Polly
            {
                public sealed class ResiliencePipelineBuilder
                {
                }
            }

            namespace Custom
            {
                public static class BuilderExtensions
                {
                    public static object Build(this ResiliencePipelineBuilder builder)
                    {
                        return new object();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR082_PerRequestResiliencePipelineAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task DoesNotReport_InTestContext()
    {
        const string source = """
            using Polly;

            public sealed class PaymentsControllerTests
            {
                [Fact]
                public void BuildsPipeline()
                {
                    var pipeline = new ResiliencePipelineBuilder().Build();
                }
            }

            public sealed class FactAttribute : System.Attribute
            {
            }

            namespace Polly
            {
                public sealed class ResiliencePipeline
                {
                }

                public sealed class ResiliencePipelineBuilder
                {
                    public ResiliencePipeline Build()
                    {
                        return new ResiliencePipeline();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzerVerifier<HCR082_PerRequestResiliencePipelineAnalyzer>.GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }
}
