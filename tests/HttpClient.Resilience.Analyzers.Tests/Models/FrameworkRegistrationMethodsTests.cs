using System.Linq;
using System.Threading;
using HttpClient.Resilience.Analyzers.Models;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace HttpClient.Resilience.Analyzers.Tests.Models;

public sealed class FrameworkRegistrationMethodsTests
{
    [Fact]
    public void AcceptsGlobalNamespaceExtension()
    {
        const string source = """
            public interface IServiceCollection
            {
            }

            public static class Extensions
            {
                public static void AddHttpClient(this IServiceCollection services)
                {
                }
            }

            public sealed class Registrations
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddHttpClient();
                }
            }
            """;

        Assert.True(IsFrameworkCall(source));
    }

    [Fact]
    public void AcceptsDependencyInjectionNamespaceExtension()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;

            public interface IServiceCollection
            {
            }

            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class Extensions
                {
                    public static void AddHttpClient(this IServiceCollection services)
                    {
                    }
                }
            }

            public sealed class Registrations
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddHttpClient();
                }
            }
            """;

        Assert.True(IsFrameworkCall(source));
    }

    [Fact]
    public void RejectsCustomNamespaceExtension()
    {
        const string source = """
            using Custom;

            public interface IServiceCollection
            {
            }

            namespace Custom
            {
                public static class Extensions
                {
                    public static void AddHttpClient(this IServiceCollection services)
                    {
                    }
                }
            }

            public sealed class Registrations
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddHttpClient();
                }
            }
            """;

        Assert.False(IsFrameworkCall(source));
    }

    [Fact]
    public void RejectsAmbiguousFrameworkAndCustomCandidates()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using Custom;

            public interface IServiceCollection
            {
            }

            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class Extensions
                {
                    public static void AddHttpClient(this IServiceCollection services)
                    {
                    }
                }
            }

            namespace Custom
            {
                public static class Extensions
                {
                    public static void AddHttpClient(this IServiceCollection services)
                    {
                    }
                }
            }

            public sealed class Registrations
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddHttpClient();
                }
            }
            """;

        Assert.False(IsFrameworkCall(source));
    }

    [Fact]
    public void AcceptsAmbiguousFrameworkCandidates()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;

            public interface IServiceCollection
            {
            }

            namespace Microsoft.Extensions.DependencyInjection
            {
                public static class Extensions
                {
                    public static void AddHttpClient(this IServiceCollection services)
                    {
                    }
                }

                public static class OtherExtensions
                {
                    public static void AddHttpClient(this IServiceCollection services)
                    {
                    }
                }
            }

            public sealed class Registrations
            {
                public void Configure(IServiceCollection services)
                {
                    services.AddHttpClient();
                }
            }
            """;

        Assert.True(IsFrameworkCall(source));
    }

    private static bool IsFrameworkCall(string source)
    {
        var compilation = TestCompilationFactory.Create("FrameworkRegistrationMethodsTests", source);
        var tree = compilation.SyntaxTrees.Single();
        var semanticModel = compilation.GetSemanticModel(tree);
        var invocation = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(candidate => candidate.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "AddHttpClient"
            });

        return FrameworkRegistrationMethods.IsFrameworkCall(invocation, semanticModel, CancellationToken.None);
    }
}
