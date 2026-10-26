using System.Collections.Immutable;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.KnownSymbols;
using HttpClient.Resilience.Analyzers.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.Lifetime;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR001_NewHttpClientInRequestPathAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR001);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression, SyntaxKind.ImplicitObjectCreationExpression);
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (BaseObjectCreationExpressionSyntax)context.Node;
        if (!IsHttpClientCreation(creation, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        if (!RequestPathContext.IsInExecutableCodeContext(creation))
        {
            return;
        }

        if (RequestPathContext.IsInTestContext(creation))
        {
            return;
        }

        if (!HasHighConfidenceRequestPathEvidence(
                creation,
                context.SemanticModel,
                context.CancellationToken))
        {
            return;
        }

        if (IsRetainedByContainingType(creation, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR001,
            creation.GetLocation()));
    }

    private static bool HasHighConfidenceRequestPathEvidence(
        SyntaxNode node,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return IsInsideLoop(node) ||
            IsDisposedInUsing(node) ||
            RequestPathContext.IsInsideMinimalApiEndpoint(node, semanticModel, cancellationToken) ||
            HealthCheckTypeContext.IsHealthCheckType(node, semanticModel, cancellationToken) ||
            RequestPathContext.IsInsideLikelyRequestPathType(node);
    }

    private static bool IsHttpClientCreation(
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var createdType = semanticModel.GetTypeInfo(creation, cancellationToken).Type;
        if (createdType is not null && createdType is not IErrorTypeSymbol)
        {
            return HttpClientSymbols.IsHttpClient(createdType);
        }

        return creation is ObjectCreationExpressionSyntax objectCreation &&
            HttpClientSymbols.IsHttpClientName(objectCreation.Type);
    }

    /// <summary>
    /// A client assigned to an instance field or property in a constructor, or to a static
    /// member in a static constructor, is retained for the containing type's lifetime. Without
    /// DI lifetime information that is not provably a per-request client, so it stays quiet.
    /// Controllers are the exception for instance state because MVC constructs them per request,
    /// and creations inside loops or lambdas are handled as per-call work rather than retained
    /// state.
    /// </summary>
    private static bool IsRetainedByContainingType(
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (IsInsideLoop(creation))
        {
            return false;
        }

        SyntaxNode current = creation;
        while (true)
        {
            switch (current.Parent)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    current = parenthesized;
                    continue;
                case CastExpressionSyntax cast:
                    current = cast;
                    continue;
                case BinaryExpressionSyntax binary when
                    binary.IsKind(SyntaxKind.CoalesceExpression) && binary.Right == current:
                    current = binary;
                    continue;
                case AssignmentExpressionSyntax assignment when
                    assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && assignment.Right == current:
                    return IsRetainedMemberAssignedInConstructor(assignment, semanticModel, cancellationToken);
                default:
                    return false;
            }
        }
    }

    private static bool IsRetainedMemberAssignedInConstructor(
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (assignment.FirstAncestorOrSelf<ConstructorDeclarationSyntax>() is not { } constructor ||
            semanticModel.GetDeclaredSymbol(constructor, cancellationToken) is not
            {
                ContainingType: { } containingType
            } constructorSymbol)
        {
            return false;
        }

        ISymbol? member = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol switch
        {
            IFieldSymbol field => field,
            IPropertySymbol property => property,
            _ => null
        };

        if (member is null ||
            !SymbolEqualityComparer.Default.Equals(member.ContainingType, containingType) ||
            member.IsStatic != constructorSymbol.IsStatic)
        {
            return false;
        }

        return member.IsStatic || !IsInsideControllerType(assignment);
    }

    private static bool IsInsideControllerType(SyntaxNode node)
    {
        return node.FirstAncestorOrSelf<TypeDeclarationSyntax>() is { } type &&
            type.Identifier.ValueText.EndsWith("Controller", System.StringComparison.Ordinal);
    }

    private static bool IsInsideLoop(SyntaxNode node)
    {
        return node.FirstAncestorOrSelf<ForStatementSyntax>() is not null ||
            node.FirstAncestorOrSelf<ForEachStatementSyntax>() is not null ||
            node.FirstAncestorOrSelf<WhileStatementSyntax>() is not null ||
            node.FirstAncestorOrSelf<DoStatementSyntax>() is not null;
    }

    private static bool IsDisposedInUsing(SyntaxNode node)
    {
        return node.FirstAncestorOrSelf<UsingStatementSyntax>() is not null ||
            node.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>()?.UsingKeyword.IsKind(SyntaxKind.UsingKeyword) == true;
    }
}
