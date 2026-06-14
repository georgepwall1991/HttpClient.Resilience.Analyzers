using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.KnownSymbols;
using HttpClient.Resilience.Analyzers.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.Lifetime;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR002_LongLivedHttpClientWithoutPooledConnectionLifetimeAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR002);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationStartAnalysisContext context)
    {
        var singletonTypes = GetKnownSingletonTypes(context.Compilation, context.CancellationToken);

        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeFieldDeclaration(nodeContext, singletonTypes),
            SyntaxKind.FieldDeclaration);
    }

    private static void AnalyzeFieldDeclaration(SyntaxNodeAnalysisContext context, IReadOnlyCollection<string> singletonTypes)
    {
        var field = (FieldDeclarationSyntax)context.Node;
        if (!IsLongLivedField(field, singletonTypes, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        foreach (var variable in field.Declaration.Variables)
        {
            if (variable.Initializer?.Value is not BaseObjectCreationExpressionSyntax creation)
            {
                continue;
            }

            if (!IsHttpClientFieldCreation(field.Declaration.Type, creation, context.SemanticModel, context.CancellationToken))
            {
                continue;
            }

            if (HasPooledConnectionLifetime(creation, context.SemanticModel, context.CancellationToken))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.HCR002,
                variable.Identifier.GetLocation()));
        }
    }

    private static IReadOnlyCollection<string> GetKnownSingletonTypes(
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        return compilation.SyntaxTrees
            .Select(tree => tree.GetRoot(cancellationToken))
            .SelectMany(ServiceRegistrationCollector.Collect)
            .Where(registration => registration.Kind == ServiceRegistrationKind.Singleton)
            .SelectMany(registration => new[]
            {
                registration.ServiceTypeName,
                registration.ImplementationTypeName
            })
            .Where(typeName => typeName is not null)
            .Select(typeName => NormalizeTypeName(typeName!))
            .ToArray();
    }

    private static bool IsLongLivedField(
        FieldDeclarationSyntax field,
        IReadOnlyCollection<string> singletonTypes,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (field.Modifiers.Any(SyntaxKind.StaticKeyword))
        {
            return true;
        }

        if (field.FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } containingType)
        {
            return false;
        }

        return containingType.Identifier.ValueText.EndsWith("Singleton", System.StringComparison.Ordinal) ||
            semanticModel.GetDeclaredSymbol(containingType, cancellationToken) is INamedTypeSymbol containingTypeSymbol &&
            singletonTypes.Any(typeName => MatchesContainingType(containingTypeSymbol, typeName));
    }

    private static bool MatchesContainingType(INamedTypeSymbol containingType, string registrationTypeName)
    {
        if (registrationTypeName.Contains("."))
        {
            return NormalizeTypeName(containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)) == registrationTypeName;
        }

        return containingType.Name == TypeNameUtilities.ToSimpleName(registrationTypeName);
    }

    private static string NormalizeTypeName(string typeName)
    {
        typeName = typeName.Trim();
        return typeName.StartsWith("global::", System.StringComparison.Ordinal)
            ? typeName.Substring("global::".Length)
            : typeName;
    }

    private static bool HasPooledConnectionLifetime(
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (creation.ArgumentList is null)
        {
            return false;
        }

        return creation.ArgumentList.Arguments
            .Any(argument => HandlerExpressionHasPooledConnectionLifetime(
                UnwrapParentheses(argument.Expression),
                semanticModel,
                cancellationToken));
    }

    private static bool HandlerExpressionHasPooledConnectionLifetime(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (expression is BaseObjectCreationExpressionSyntax handlerCreation)
        {
            return IsConfiguredSocketsHttpHandlerCreation(handlerCreation, semanticModel, cancellationToken);
        }

        return semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol switch
        {
            IFieldSymbol field => SymbolInitializerHasPooledConnectionLifetime(field, semanticModel, cancellationToken),
            ILocalSymbol local => SymbolInitializerHasPooledConnectionLifetime(local, semanticModel, cancellationToken),
            _ => false
        };
    }

    private static bool SymbolInitializerHasPooledConnectionLifetime(
        ISymbol symbol,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return symbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable => variable.Initializer?.Value is BaseObjectCreationExpressionSyntax handlerCreation &&
                IsConfiguredSocketsHttpHandlerCreation(handlerCreation, semanticModel, cancellationToken));
    }

    private static bool IsConfiguredSocketsHttpHandlerCreation(
        BaseObjectCreationExpressionSyntax handlerCreation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return IsSocketsHttpHandlerCreation(handlerCreation, semanticModel, cancellationToken) &&
            handlerCreation.Initializer?.Expressions
                .OfType<AssignmentExpressionSyntax>()
                .Any(assignment => IsPooledConnectionLifetimeMember(assignment.Left)) == true;
    }

    private static bool IsPooledConnectionLifetimeMember(ExpressionSyntax expression)
    {
        expression = UnwrapParentheses(expression);

        return expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "PooledConnectionLifetime",
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText == "PooledConnectionLifetime",
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

    private static bool IsHttpClientFieldCreation(
        TypeSyntax fieldType,
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var createdType = semanticModel.GetTypeInfo(creation, cancellationToken).Type;
        if (createdType is not null && createdType is not IErrorTypeSymbol)
        {
            return HttpClientSymbols.IsHttpClient(createdType);
        }

        var declaredType = semanticModel.GetTypeInfo(fieldType, cancellationToken).Type;
        if (declaredType is not null && declaredType is not IErrorTypeSymbol)
        {
            return HttpClientSymbols.IsHttpClient(declaredType);
        }

        if (HttpClientSymbols.IsHttpClientName(fieldType))
        {
            return true;
        }

        return creation is ObjectCreationExpressionSyntax objectCreation &&
            HttpClientSymbols.IsHttpClientName(objectCreation.Type);
    }

    private static bool IsSocketsHttpHandlerCreation(
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var createdType = semanticModel.GetTypeInfo(creation, cancellationToken).Type;
        if (createdType is not null && createdType is not IErrorTypeSymbol)
        {
            return HttpClientSymbols.IsSocketsHttpHandler(createdType);
        }

        return creation is ObjectCreationExpressionSyntax objectCreation &&
            HttpClientSymbols.IsSocketsHttpHandlerName(objectCreation.Type);
    }
}
