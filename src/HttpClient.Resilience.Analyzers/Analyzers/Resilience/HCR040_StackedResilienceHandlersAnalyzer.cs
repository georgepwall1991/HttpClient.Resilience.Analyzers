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
public sealed class HCR040_StackedResilienceHandlersAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR040);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!IsDuplicateResilienceHandlerInChain(invocation, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR040,
            memberAccess.Name.GetLocation()));
    }

    private static bool IsDuplicateResilienceHandlerInChain(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (!ChainLooksLikeHttpClientBuilder(invocation, semanticModel, cancellationToken))
        {
            return false;
        }

        return IsAddStandardResilienceHandlerInvocation(invocation, semanticModel, cancellationToken) &&
            (CountStandardResilienceHandlersInChain(invocation, semanticModel, cancellationToken) > 1 ||
                IsDuplicateStandardHandlerOnSameBuilderInBlock(invocation, semanticModel, cancellationToken)) ||
            IsDuplicateNamedResilienceHandlerInChain(invocation, semanticModel, cancellationToken);
    }

    private static int CountStandardResilienceHandlersInChain(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var count = 0;
        var current = expression;

        while (current is InvocationExpressionSyntax invocation)
        {
            if (IsAddStandardResilienceHandlerInvocation(invocation, semanticModel, cancellationToken))
            {
                count++;
            }

            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                current = memberAccess.Expression;
                continue;
            }

            break;
        }

        return count;
    }

    private static bool IsAddStandardResilienceHandlerInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "AddStandardResilienceHandler"
            })
        {
            return false;
        }

        return IsFrameworkResilienceExtension(invocation, semanticModel, cancellationToken);
    }

    private static bool IsFrameworkResilienceExtension(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return IsFrameworkResilienceExtension(method);
        }

        var candidateMethods = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
        return candidateMethods.Length == 0 || candidateMethods.All(IsFrameworkResilienceExtension);
    }

    private static bool IsFrameworkResilienceExtension(IMethodSymbol method)
    {
        var containingNamespace = (method.ReducedFrom ?? method).ContainingNamespace;
        return containingNamespace.IsGlobalNamespace ||
            containingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";
    }

    private static bool IsDuplicateStandardHandlerOnSameBuilderInBlock(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            !IsHttpClientBuilderReceiver(memberAccess.Expression, semanticModel, cancellationToken) ||
            invocation.FirstAncestorOrSelf<BlockSyntax>() is not { } block)
        {
            return false;
        }

        return block
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(candidate => candidate.SpanStart < invocation.SpanStart)
            .Any(candidate => IsAddStandardResilienceHandlerInvocation(candidate, semanticModel, cancellationToken) &&
                candidate.Expression is MemberAccessExpressionSyntax candidateMemberAccess &&
                ReceiverMatches(
                    memberAccess.Expression,
                    candidateMemberAccess.Expression,
                    semanticModel,
                    cancellationToken) &&
                !ReceiverIsReassignedBetweenInvocations(
                    memberAccess.Expression,
                    candidate,
                    invocation,
                    block,
                    semanticModel,
                    cancellationToken));
    }

    private static bool ReceiverMatches(
        ExpressionSyntax receiver,
        ExpressionSyntax candidateReceiver,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var receiverSymbol = GetReceiverSymbol(receiver, semanticModel, cancellationToken);
        var candidateReceiverSymbol = GetReceiverSymbol(candidateReceiver, semanticModel, cancellationToken);

        if (receiverSymbol is not null && candidateReceiverSymbol is not null)
        {
            return SymbolEqualityComparer.Default.Equals(receiverSymbol, candidateReceiverSymbol);
        }

        return receiver.ToString() == candidateReceiver.ToString();
    }

    private static bool ReceiverIsReassignedBetweenInvocations(
        ExpressionSyntax receiver,
        InvocationExpressionSyntax previousInvocation,
        InvocationExpressionSyntax currentInvocation,
        BlockSyntax block,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var receiverSymbol = GetReceiverSymbol(receiver, semanticModel, cancellationToken);
        if (receiverSymbol is null)
        {
            return false;
        }

        return block
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.SpanStart > previousInvocation.SpanStart &&
                assignment.SpanStart < currentInvocation.SpanStart &&
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                SymbolEqualityComparer.Default.Equals(
                    GetReceiverSymbol(assignment.Left, semanticModel, cancellationToken),
                    receiverSymbol));
    }

    private static ISymbol? GetReceiverSymbol(
        ExpressionSyntax receiver,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return semanticModel.GetSymbolInfo(receiver, cancellationToken).Symbol switch
        {
            ILocalSymbol local => local,
            IParameterSymbol parameter => parameter,
            IFieldSymbol field => field,
            IPropertySymbol property => property,
            _ => null
        };
    }

    private static bool IsDuplicateNamedResilienceHandlerInChain(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (!TryGetAddResilienceHandlerName(invocation, semanticModel, cancellationToken, out var handlerName))
        {
            return false;
        }

        return GetInvocationChain(invocation)
            .Count(candidate => TryGetAddResilienceHandlerName(
                    candidate,
                    semanticModel,
                    cancellationToken,
                    out var candidateName) &&
                candidateName == handlerName) > 1;
    }

    private static IEnumerable<InvocationExpressionSyntax> GetInvocationChain(ExpressionSyntax expression)
    {
        var current = expression;

        while (current is InvocationExpressionSyntax invocation)
        {
            yield return invocation;

            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                yield break;
            }

            current = memberAccess.Expression;
        }
    }

    private static bool TryGetAddResilienceHandlerName(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out string? handlerName)
    {
        handlerName = null;

        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "AddResilienceHandler"
            } ||
            invocation.ArgumentList.Arguments.Count == 0)
        {
            return false;
        }

        handlerName = TryGetStringConstant(
            invocation.ArgumentList.Arguments[0].Expression,
            semanticModel,
            cancellationToken);
        return handlerName is not null;
    }

    private static string? TryGetStringConstant(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        expression = UnwrapParentheses(expression);

        if (expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return literal.Token.ValueText;
        }

        var constant = semanticModel.GetConstantValue(expression, cancellationToken);
        return constant.HasValue && constant.Value is string value
            ? value
            : null;
    }

    private static bool ChainLooksLikeHttpClientBuilder(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return GetInvocationChain(invocation).Any(candidate =>
            IsAddHttpClientInvocation(candidate) &&
            IsVisibleHttpClientBuilderType(semanticModel.GetTypeInfo(candidate, cancellationToken).Type) ||
            candidate.Expression is MemberAccessExpressionSyntax memberAccess &&
            IsHttpClientBuilderReceiver(memberAccess.Expression, semanticModel, cancellationToken));
    }

    private static bool IsAddHttpClientInvocation(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is MemberAccessExpressionSyntax
        {
            Name.Identifier.ValueText: "AddHttpClient"
        };
    }

    private static bool IsHttpClientBuilderReceiver(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (IsHttpClientBuilderType(semanticModel.GetTypeInfo(expression, cancellationToken).Type))
        {
            return true;
        }

        return semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol switch
        {
            ILocalSymbol local => IsHttpClientBuilderType(local.Type),
            IParameterSymbol parameter => IsHttpClientBuilderType(parameter.Type),
            IFieldSymbol field => IsHttpClientBuilderType(field.Type),
            IPropertySymbol property => IsHttpClientBuilderType(property.Type),
            _ => false
        } || SyntacticReceiverLooksLikeHttpClientBuilder(expression);
    }

    private static bool IsHttpClientBuilderType(ITypeSymbol? type)
    {
        return type is not null &&
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::Microsoft.Extensions.DependencyInjection.IHttpClientBuilder";
    }

    private static bool IsVisibleHttpClientBuilderType(ITypeSymbol? type)
    {
        return IsHttpClientBuilderType(type) ||
            type is INamedTypeSymbol
            {
                Name: "IHttpClientBuilder",
                ContainingNamespace.IsGlobalNamespace: true
            };
    }

    private static bool SyntacticReceiverLooksLikeHttpClientBuilder(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => ParameterLooksLikeHttpClientBuilder(identifier) ||
                LocalLooksLikeHttpClientBuilder(identifier) ||
                FieldOrPropertyLooksLikeHttpClientBuilder(identifier),
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax name } =>
                FieldOrPropertyLooksLikeHttpClientBuilder(name),
            _ => false
        };
    }

    private static bool ParameterLooksLikeHttpClientBuilder(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>()?
            .ParameterList.Parameters
            .Any(parameter => parameter.Identifier.ValueText == identifier.Identifier.ValueText &&
                parameter.Type is not null &&
                IsHttpClientBuilderTypeName(parameter.Type)) == true;
    }

    private static bool LocalLooksLikeHttpClientBuilder(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<BlockSyntax>()?
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText &&
                variable.Parent is VariableDeclarationSyntax declaration &&
                IsHttpClientBuilderTypeName(declaration.Type)) == true;
    }

    private static bool FieldOrPropertyLooksLikeHttpClientBuilder(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<TypeDeclarationSyntax>()?
            .Members
            .Any(member => member switch
            {
                FieldDeclarationSyntax field => IsHttpClientBuilderTypeName(field.Declaration.Type) &&
                    field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText),
                PropertyDeclarationSyntax property => IsHttpClientBuilderTypeName(property.Type) &&
                    property.Identifier.ValueText == identifier.Identifier.ValueText,
                _ => false
            }) == true;
    }

    private static bool IsHttpClientBuilderTypeName(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "IHttpClientBuilder",
            QualifiedNameSyntax qualified => qualified.ToString() == "Microsoft.Extensions.DependencyInjection.IHttpClientBuilder" ||
                qualified.ToString() == "global::Microsoft.Extensions.DependencyInjection.IHttpClientBuilder",
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.ToString() == "global::Microsoft.Extensions.DependencyInjection.IHttpClientBuilder",
            _ => false
        };
    }

    private static ExpressionSyntax UnwrapParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }
}
