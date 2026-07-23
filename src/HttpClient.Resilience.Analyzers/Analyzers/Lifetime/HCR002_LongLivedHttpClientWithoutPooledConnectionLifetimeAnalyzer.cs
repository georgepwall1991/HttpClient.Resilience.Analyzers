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
        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzePropertyDeclaration(nodeContext, singletonTypes),
            SyntaxKind.PropertyDeclaration);
        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeAssignment(nodeContext, singletonTypes),
            SyntaxKind.SimpleAssignmentExpression);
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
            if (variable.Initializer?.Value is not { } initializer ||
                UnwrapTransparentExpressions(initializer) is not BaseObjectCreationExpressionSyntax creation)
            {
                continue;
            }

            if (!IsHttpClientMemberCreation(field.Declaration.Type, creation, context.SemanticModel, context.CancellationToken))
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

    private static void AnalyzePropertyDeclaration(SyntaxNodeAnalysisContext context, IReadOnlyCollection<string> singletonTypes)
    {
        var property = (PropertyDeclarationSyntax)context.Node;
        if (!IsLongLivedProperty(property, singletonTypes, context.SemanticModel, context.CancellationToken) ||
            property.Initializer?.Value is not { } initializer ||
            UnwrapTransparentExpressions(initializer) is not BaseObjectCreationExpressionSyntax creation)
        {
            return;
        }

        if (!IsHttpClientMemberCreation(property.Type, creation, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        if (HasPooledConnectionLifetime(creation, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR002,
            property.Identifier.GetLocation()));
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context, IReadOnlyCollection<string> singletonTypes)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        if (!TryGetHttpClientCreationExpression(
                assignment.Right,
                assignment,
                context.SemanticModel,
                context.CancellationToken,
                out var creation))
        {
            return;
        }

        if (!AssignmentTargetsLongLivedHttpClientMember(
                assignment.Left,
                creation,
                singletonTypes,
                context.SemanticModel,
                context.CancellationToken) ||
            HasPooledConnectionLifetime(creation, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HCR002,
            assignment.Left.GetLocation()));
    }

    private static bool TryGetHttpClientCreationExpression(
        ExpressionSyntax expression,
        SyntaxNode context,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out BaseObjectCreationExpressionSyntax creation)
    {
        expression = UnwrapTransparentExpressions(expression);

        if (expression is BaseObjectCreationExpressionSyntax directCreation)
        {
            creation = directCreation;
            return true;
        }

        if (expression is IdentifierNameSyntax identifier &&
            TryGetLocalHttpClientCreation(identifier, context, semanticModel, cancellationToken, out creation))
        {
            return true;
        }

        creation = null!;
        return false;
    }

    private static bool TryGetLocalHttpClientCreation(
        IdentifierNameSyntax identifier,
        SyntaxNode context,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out BaseObjectCreationExpressionSyntax creation)
    {
        creation = null!;

        var containingBlock = context.FirstAncestorOrSelf<BlockSyntax>();
        if (containingBlock is null)
        {
            return false;
        }

        var referencedSymbol = semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
        if (referencedSymbol is not ILocalSymbol)
        {
            return false;
        }

        var variable = containingBlock
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(candidate =>
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetDeclaredSymbol(candidate, cancellationToken),
                    referencedSymbol) &&
                candidate.SpanStart < context.SpanStart &&
                candidate.Initializer is not null &&
                !LocalIsReassignedBetweenDeclarationAndUse(
                    containingBlock,
                    candidate,
                    context,
                    semanticModel,
                    cancellationToken));

        if (variable?.Initializer?.Value is not { } initializer ||
            UnwrapTransparentExpressions(initializer) is not BaseObjectCreationExpressionSyntax localCreation)
        {
            return false;
        }

        creation = localCreation;
        return true;
    }

    private static bool LocalIsReassignedBetweenDeclarationAndUse(
        BlockSyntax containingBlock,
        VariableDeclaratorSyntax variable,
        SyntaxNode context,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var localSymbol = semanticModel.GetDeclaredSymbol(variable, cancellationToken);

        return containingBlock
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.SpanStart > variable.SpanStart &&
                assignment.SpanStart < context.SpanStart &&
                assignment.Left is IdentifierNameSyntax identifier &&
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol,
                    localSymbol));
    }

    private static bool AssignmentTargetsLongLivedHttpClientMember(
        ExpressionSyntax target,
        BaseObjectCreationExpressionSyntax creation,
        IReadOnlyCollection<string> singletonTypes,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var targetSymbol = semanticModel.GetSymbolInfo(target, cancellationToken).Symbol;
        if (targetSymbol is IFieldSymbol field)
        {
            return IsLongLivedField(field, singletonTypes) &&
                IsHttpClientCreation(field, creation, semanticModel, cancellationToken);
        }

        if (targetSymbol is IPropertySymbol property)
        {
            return IsLongLivedProperty(property, singletonTypes) &&
                IsHttpClientCreation(property, creation, semanticModel, cancellationToken);
        }

        if (targetSymbol is ILocalSymbol)
        {
            return false;
        }

        if (TryGetVisibleAssignedField(target) is { } fieldDeclaration)
        {
            return IsLongLivedField(fieldDeclaration, singletonTypes, semanticModel, cancellationToken) &&
                IsHttpClientMemberCreation(fieldDeclaration.Declaration.Type, creation, semanticModel, cancellationToken);
        }

        return TryGetVisibleAssignedProperty(target) is { } propertyDeclaration &&
            IsLongLivedProperty(propertyDeclaration, singletonTypes, semanticModel, cancellationToken) &&
            IsHttpClientMemberCreation(propertyDeclaration.Type, creation, semanticModel, cancellationToken);
    }

    private static FieldDeclarationSyntax? TryGetVisibleAssignedField(ExpressionSyntax target)
    {
        var fieldName = target switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            _ => null
        };

        if (fieldName is null)
        {
            return null;
        }

        return target.FirstAncestorOrSelf<TypeDeclarationSyntax>()?
            .Members
            .OfType<FieldDeclarationSyntax>()
            .FirstOrDefault(field => field.Declaration.Variables.Any(variable =>
                variable.Identifier.ValueText == fieldName));
    }

    private static PropertyDeclarationSyntax? TryGetVisibleAssignedProperty(ExpressionSyntax target)
    {
        var propertyName = target switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            _ => null
        };

        if (propertyName is null)
        {
            return null;
        }

        return target.FirstAncestorOrSelf<TypeDeclarationSyntax>()?
            .Members
            .OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault(property => property.Identifier.ValueText == propertyName);
    }

    private static IReadOnlyCollection<string> GetKnownSingletonTypes(
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        return compilation.SyntaxTrees
            .SelectMany(tree => ServiceRegistrationCollector.CollectFrameworkRegistrations(
                tree.GetRoot(cancellationToken),
                GetSemanticModel(compilation, tree),
                cancellationToken))
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

#pragma warning disable RS1030 // HCR002 performs compilation-wide singleton matching and needs cross-tree semantic checks.
    private static SemanticModel GetSemanticModel(Compilation compilation, SyntaxTree syntaxTree)
    {
        return compilation.GetSemanticModel(syntaxTree);
    }
#pragma warning restore RS1030

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
            IsKnownSingletonType(containingTypeSymbol, singletonTypes);
    }

    private static bool IsLongLivedField(IFieldSymbol field, IReadOnlyCollection<string> singletonTypes)
    {
        return field.IsStatic ||
            field.ContainingType.Name.EndsWith("Singleton", System.StringComparison.Ordinal) ||
            IsKnownSingletonType(field.ContainingType, singletonTypes);
    }

    private static bool IsLongLivedProperty(
        PropertyDeclarationSyntax property,
        IReadOnlyCollection<string> singletonTypes,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (property.Modifiers.Any(SyntaxKind.StaticKeyword))
        {
            return true;
        }

        if (property.FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } containingType)
        {
            return false;
        }

        return containingType.Identifier.ValueText.EndsWith("Singleton", System.StringComparison.Ordinal) ||
            semanticModel.GetDeclaredSymbol(containingType, cancellationToken) is INamedTypeSymbol containingTypeSymbol &&
            IsKnownSingletonType(containingTypeSymbol, singletonTypes);
    }

    private static bool IsLongLivedProperty(IPropertySymbol property, IReadOnlyCollection<string> singletonTypes)
    {
        return property.IsStatic ||
            property.ContainingType.Name.EndsWith("Singleton", System.StringComparison.Ordinal) ||
            IsKnownSingletonType(property.ContainingType, singletonTypes);
    }

    private static bool IsKnownSingletonType(INamedTypeSymbol containingType, IReadOnlyCollection<string> singletonTypes)
    {
        return singletonTypes.Any(typeName => MatchesContainingType(containingType, typeName));
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
                UnwrapTransparentExpressions(argument.Expression),
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
            ILocalSymbol local => LocalInitializerHasPooledConnectionLifetime(
                expression,
                local,
                semanticModel,
                cancellationToken),
            IPropertySymbol property => PropertyInitializerHasPooledConnectionLifetime(property, semanticModel, cancellationToken),
            _ => false
        };
    }

    private static bool LocalInitializerHasPooledConnectionLifetime(
        ExpressionSyntax expression,
        ILocalSymbol local,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var containingBlock = expression.FirstAncestorOrSelf<BlockSyntax>();
        if (containingBlock is null)
        {
            return false;
        }

        return containingBlock
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable =>
                variable.SpanStart < expression.SpanStart &&
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetDeclaredSymbol(variable, cancellationToken),
                    local) &&
                variable.Initializer?.Value is { } initializer &&
                UnwrapTransparentExpressions(initializer) is BaseObjectCreationExpressionSyntax handlerCreation &&
                !LocalIsReassignedBetweenDeclarationAndUse(
                    containingBlock,
                    variable,
                    expression,
                    semanticModel,
                    cancellationToken) &&
                IsSocketsHttpHandlerCreation(handlerCreation, semanticModel, cancellationToken) &&
                (HasPooledConnectionLifetimeInitializer(handlerCreation) ||
                    LocalHasPooledConnectionLifetimeAssignmentBeforeUse(
                        containingBlock,
                        local,
                        variable.SpanStart,
                        expression.SpanStart,
                        semanticModel,
                        cancellationToken)));
    }

    private static bool SymbolInitializerHasPooledConnectionLifetime(
        ISymbol symbol,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return symbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable => variable.Initializer?.Value is { } initializer &&
                UnwrapTransparentExpressions(initializer) is BaseObjectCreationExpressionSyntax handlerCreation &&
                IsConfiguredSocketsHttpHandlerCreation(handlerCreation, semanticModel, cancellationToken));
    }

    private static bool PropertyInitializerHasPooledConnectionLifetime(
        IPropertySymbol property,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return property.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<PropertyDeclarationSyntax>()
            .Any(propertyDeclaration => propertyDeclaration.Initializer?.Value is { } initializer &&
                UnwrapTransparentExpressions(initializer) is BaseObjectCreationExpressionSyntax handlerCreation &&
                IsConfiguredSocketsHttpHandlerCreation(handlerCreation, semanticModel, cancellationToken));
    }

    private static bool IsConfiguredSocketsHttpHandlerCreation(
        BaseObjectCreationExpressionSyntax handlerCreation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return IsSocketsHttpHandlerCreation(handlerCreation, semanticModel, cancellationToken) &&
            HasPooledConnectionLifetimeInitializer(handlerCreation);
    }

    private static bool HasPooledConnectionLifetimeInitializer(BaseObjectCreationExpressionSyntax handlerCreation)
    {
        return handlerCreation.Initializer?.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => IsPooledConnectionLifetimeMember(assignment.Left)) == true;
    }

    private static bool LocalHasPooledConnectionLifetimeAssignmentBeforeUse(
        BlockSyntax containingBlock,
        ILocalSymbol local,
        int declarationStart,
        int useStart,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return containingBlock
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.SpanStart > declarationStart &&
                assignment.SpanStart < useStart &&
                assignment.Left is MemberAccessExpressionSyntax memberAccess &&
                IsPooledConnectionLifetimeMember(memberAccess.Name) &&
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol,
                    local));
    }

    private static bool IsPooledConnectionLifetimeMember(ExpressionSyntax expression)
    {
        expression = UnwrapTransparentExpressions(expression);

        return expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "PooledConnectionLifetime",
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText == "PooledConnectionLifetime",
            _ => false
        };
    }

    private static ExpressionSyntax UnwrapTransparentExpressions(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax postfix when
                    postfix.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SuppressNullableWarningExpression):
                    expression = postfix.Operand;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private static bool IsHttpClientMemberCreation(
        TypeSyntax memberType,
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var createdType = semanticModel.GetTypeInfo(creation, cancellationToken).Type;
        if (createdType is not null && createdType is not IErrorTypeSymbol)
        {
            return HttpClientSymbols.IsHttpClient(createdType);
        }

        var declaredType = semanticModel.GetTypeInfo(memberType, cancellationToken).Type;
        if (declaredType is not null && declaredType is not IErrorTypeSymbol)
        {
            return HttpClientSymbols.IsHttpClient(declaredType);
        }

        if (HttpClientSymbols.IsHttpClientName(memberType))
        {
            return true;
        }

        return creation is ObjectCreationExpressionSyntax objectCreation &&
            HttpClientSymbols.IsHttpClientName(objectCreation.Type);
    }

    private static bool IsHttpClientCreation(
        IFieldSymbol field,
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (!HttpClientSymbols.IsHttpClient(field.Type) &&
            !FieldSyntaxTypeLooksLikeHttpClient(field, cancellationToken))
        {
            return false;
        }

        var createdType = semanticModel.GetTypeInfo(creation, cancellationToken).Type;
        return createdType is null ||
            createdType is IErrorTypeSymbol ||
            HttpClientSymbols.IsHttpClient(createdType);
    }

    private static bool IsHttpClientCreation(
        IPropertySymbol property,
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (!HttpClientSymbols.IsHttpClient(property.Type) &&
            !PropertySyntaxTypeLooksLikeHttpClient(property, cancellationToken))
        {
            return false;
        }

        var createdType = semanticModel.GetTypeInfo(creation, cancellationToken).Type;
        return createdType is null ||
            createdType is IErrorTypeSymbol ||
            HttpClientSymbols.IsHttpClient(createdType);
    }

    private static bool FieldSyntaxTypeLooksLikeHttpClient(
        IFieldSymbol field,
        System.Threading.CancellationToken cancellationToken)
    {
        return field.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable => variable.Parent is VariableDeclarationSyntax declaration &&
                HttpClientSymbols.IsHttpClientName(declaration.Type));
    }

    private static bool PropertySyntaxTypeLooksLikeHttpClient(
        IPropertySymbol property,
        System.Threading.CancellationToken cancellationToken)
    {
        return property.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<PropertyDeclarationSyntax>()
            .Any(propertyDeclaration => HttpClientSymbols.IsHttpClientName(propertyDeclaration.Type));
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
