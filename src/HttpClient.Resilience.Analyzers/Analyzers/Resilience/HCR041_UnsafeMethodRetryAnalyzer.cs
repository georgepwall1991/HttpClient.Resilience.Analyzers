using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.Resilience;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR041_UnsafeMethodRetryAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] UnsafeHttpMethodPrefixes =
    {
        "Delete",
        "Patch",
        "Post",
        "Put"
    };

    private static readonly string[] UnsafeHttpMethodNames =
    {
        "Connect",
        "Delete",
        "Patch",
        "Post",
        "Put"
    };

    private static readonly string[] SafeHttpMethodNames =
    {
        "Get",
        "Head",
        "Options",
        "Trace"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR041);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationStartAnalysisContext context)
    {
        var roots = context.Compilation.SyntaxTrees
            .Select(tree => tree.GetRoot(context.CancellationToken))
            .ToArray();

        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeInvocation(nodeContext, roots),
            SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, IEnumerable<SyntaxNode> roots)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!IsAddStandardResilienceHandlerInvocation(invocation) ||
            HasUnsafeMethodRetryGuard(invocation))
        {
            return;
        }

        var typedClient = FindTypedClientInChain(invocation);

        if (typedClient is not null && TypedClientSendsUnsafeHttpMethod(roots, typedClient))
        {
            ReportDiagnostic(context, invocation);
            return;
        }

        var namedClient = FindNamedClientInChain(invocation);
        if (namedClient is not null && NamedClientSendsUnsafeHttpMethod(roots, namedClient))
        {
            ReportDiagnostic(context, invocation);
        }
    }

    private static void ReportDiagnostic(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
    {
        var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR041,
            memberAccess.Name.GetLocation()));
    }

    private static bool IsAddStandardResilienceHandlerInvocation(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is MemberAccessExpressionSyntax
        {
            Name.Identifier.ValueText: "AddStandardResilienceHandler"
        };
    }

    private static bool HasUnsafeMethodRetryGuard(InvocationExpressionSyntax invocation)
    {
        return ContainsDisableForUnsafeHttpMethods(invocation) ||
            ContainsSafeOnlyRetryPredicate(invocation);
    }

    private static bool ContainsDisableForUnsafeHttpMethods(InvocationExpressionSyntax invocation)
    {
        return invocation
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(child => child.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "DisableForUnsafeHttpMethods"
            });
    }

    private static bool ContainsSafeOnlyRetryPredicate(InvocationExpressionSyntax invocation)
    {
        return invocation
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(IsSafeOnlyShouldHandleAssignment);
    }

    private static bool IsSafeOnlyShouldHandleAssignment(AssignmentExpressionSyntax assignment)
    {
        if (assignment.Left is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "ShouldHandle"
            })
        {
            return false;
        }

        var httpMethods = assignment.Right
            .DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Where(memberAccess => memberAccess.Expression.ToString() == "HttpMethod")
            .Select(memberAccess => memberAccess.Name.Identifier.ValueText)
            .ToArray();

        return httpMethods.Any(method => SafeHttpMethodNames.Contains(method, System.StringComparer.Ordinal)) &&
            !httpMethods.Any(method => UnsafeHttpMethodNames.Contains(method, System.StringComparer.Ordinal));
    }

    private static string? FindTypedClientInChain(InvocationExpressionSyntax invocation)
    {
        ExpressionSyntax current = invocation;

        while (current is InvocationExpressionSyntax currentInvocation)
        {
            if (currentInvocation.Expression is MemberAccessExpressionSyntax
                {
                    Name: GenericNameSyntax
                    {
                        Identifier.ValueText: "AddHttpClient",
                        TypeArgumentList.Arguments.Count: 1
                    } genericName
                })
            {
                return genericName.TypeArgumentList.Arguments[0].ToString();
            }

            if (currentInvocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                break;
            }

            current = memberAccess.Expression;
        }

        return null;
    }

    private static string? FindNamedClientInChain(InvocationExpressionSyntax invocation)
    {
        ExpressionSyntax current = invocation;

        while (current is InvocationExpressionSyntax currentInvocation)
        {
            if (currentInvocation.Expression is MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: "AddHttpClient"
                } &&
                currentInvocation.ArgumentList.Arguments.Count > 0 &&
                TryGetStringLiteral(currentInvocation.ArgumentList.Arguments[0].Expression) is { } clientName)
            {
                return clientName;
            }

            if (currentInvocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                break;
            }

            current = memberAccess.Expression;
        }

        return null;
    }

    private static bool TypedClientSendsUnsafeHttpMethod(IEnumerable<SyntaxNode> roots, string typedClient)
    {
        return roots
            .SelectMany(root => root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(type => type.Identifier.ValueText == typedClient)
            .SelectMany(type => type.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Any(IsUnsafeHttpCall);
    }

    private static bool NamedClientSendsUnsafeHttpMethod(IEnumerable<SyntaxNode> roots, string clientName)
    {
        foreach (var invocation in roots
            .SelectMany(root => root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Where(invocation => IsCreateClientInvocation(invocation, clientName)))
        {
            if (IsDirectUnsafeCall(invocation) || AssignedClientSendsUnsafeHttpMethod(invocation, clientName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnsafeHttpCall(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        var methodName = memberAccess.Name.Identifier.ValueText;
        return UnsafeHttpMethodPrefixes.Any(prefix => methodName.StartsWith(prefix, System.StringComparison.Ordinal));
    }

    private static bool IsCreateClientInvocation(InvocationExpressionSyntax invocation, string clientName)
    {
        return invocation.Expression is MemberAccessExpressionSyntax
        {
            Name.Identifier.ValueText: "CreateClient"
        } &&
        invocation.ArgumentList.Arguments.Count > 0 &&
        TryGetStringLiteral(invocation.ArgumentList.Arguments[0].Expression) == clientName;
    }

    private static bool IsDirectUnsafeCall(InvocationExpressionSyntax createClientInvocation)
    {
        return createClientInvocation.Parent is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Parent is InvocationExpressionSyntax invocation &&
            IsUnsafeHttpCall(invocation);
    }

    private static bool AssignedClientSendsUnsafeHttpMethod(InvocationExpressionSyntax createClientInvocation, string clientName)
    {
        var declarator = createClientInvocation.FirstAncestorOrSelf<VariableDeclaratorSyntax>();
        if (declarator is null)
        {
            return false;
        }

        var localName = declarator.Identifier.ValueText;
        var containingBlock = declarator.FirstAncestorOrSelf<BlockSyntax>();
        if (containingBlock is null)
        {
            return false;
        }

        return containingBlock
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => invocation.Expression is MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax identifier
            } && identifier.Identifier.ValueText == localName && IsUnsafeHttpCall(invocation));
    }

    private static string? TryGetStringLiteral(ExpressionSyntax expression)
    {
        return expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;
    }
}
