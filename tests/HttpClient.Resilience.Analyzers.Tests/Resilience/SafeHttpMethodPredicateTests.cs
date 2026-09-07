using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HttpClient.Resilience.Analyzers.Models;
using HttpClient.Resilience.Analyzers.Tests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace HttpClient.Resilience.Analyzers.Tests.Resilience;

public sealed class SafeHttpMethodPredicateTests
{
    [Fact]
    public async Task RejectsAssignmentTargetWithoutShouldHandleMember()
    {
        const string source = """
            using System.Net.Http;

            public sealed class PredicateArgs
            {
                public HttpMethod Method { get; set; } = HttpMethod.Get;
            }

            public sealed class RetryOptions
            {
                public System.Action<PredicateArgs>? Retry { get; set; }
            }

            public static class Registrations
            {
                public static void Configure(RetryOptions options)
                {
                    options.Retry = args => args.Method == HttpMethod.Get;
                }
            }
            """;

        var compilation = TestCompilationFactory.Create("PredicateTests", source);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var assignment = tree.GetRoot().DescendantNodes().OfType<AssignmentExpressionSyntax>().Single();

        Assert.False(SafeHttpMethodPredicate.IsSafeOnlyShouldHandleAssignment(
            assignment,
            model,
            CancellationToken.None,
            "Polly.Retry",
            null));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task RejectsUnresolvableOwnerForRequiredOwnerName()
    {
        const string source = """
            using System.Net.Http;

            public sealed class PredicateArgs
            {
                public HttpMethod Method { get; set; } = HttpMethod.Get;
            }

            public sealed class RetryOptions
            {
                public System.Func<PredicateArgs, bool>? ShouldHandle { get; set; }
            }

            public static class Registrations
            {
                public static void Configure(RetryOptions options)
                {
                    options.ShouldHandle = args => args.Method == HttpMethod.Get;
                }
            }
            """;

        var compilation = TestCompilationFactory.Create("PredicateTests", source);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var assignment = tree.GetRoot().DescendantNodes().OfType<AssignmentExpressionSyntax>().Single();

        Assert.False(SafeHttpMethodPredicate.IsSafeOnlyShouldHandleAssignment(
            assignment,
            model,
            CancellationToken.None,
            "Polly.Retry",
            "Retry"));
        await Task.CompletedTask;
    }
}
