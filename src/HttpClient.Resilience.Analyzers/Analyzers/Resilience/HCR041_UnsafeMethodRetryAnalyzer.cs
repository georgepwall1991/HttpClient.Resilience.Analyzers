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

namespace HttpClient.Resilience.Analyzers.Analyzers.Resilience;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR041_UnsafeMethodRetryAnalyzer : DiagnosticAnalyzer
{
    private sealed class TypedClientRegistration
    {
        public TypedClientRegistration(string rawTypeName, string? resolvedTypeName)
        {
            RawTypeName = rawTypeName;
            ResolvedTypeName = resolvedTypeName;
        }

        public string RawTypeName { get; }

        public string? ResolvedTypeName { get; }
    }

    private static readonly string[] UnsafeHttpMethodPrefixes =
    {
        "Connect",
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

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        IEnumerable<SyntaxNode> roots)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!IsAddStandardResilienceHandlerInvocation(
                invocation,
                context.SemanticModel,
                context.CancellationToken) ||
            HasUnsafeMethodRetryGuard(
                invocation,
                context.SemanticModel,
                context.CancellationToken))
        {
            return;
        }

        var typedClient = FindTypedClientImplementationInChain(invocation, context.SemanticModel, context.CancellationToken);
        typedClient ??= FindTypedClientImplementationForBuilderLocal(
            invocation,
            context.SemanticModel,
            context.CancellationToken);

        if (typedClient is not null && TypedClientSendsUnsafeHttpMethod(roots, typedClient))
        {
            ReportDiagnostic(context, invocation);
            return;
        }

        var namedClient = FindNamedClientInChain(invocation, context.SemanticModel, context.CancellationToken);
        namedClient ??= FindNamedClientForBuilderLocal(invocation, context.SemanticModel, context.CancellationToken);
        if (namedClient is not null &&
            NamedClientSendsUnsafeHttpMethod(roots, namedClient))
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

    private static bool HasUnsafeMethodRetryGuard(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return ContainsDisableForUnsafeHttpMethods(invocation, semanticModel, cancellationToken) ||
            ContainsSafeOnlyRetryPredicate(invocation);
    }

    private static bool ContainsDisableForUnsafeHttpMethods(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return invocation
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(child => IsDisableForUnsafeHttpMethodsInvocation(child, semanticModel, cancellationToken));
    }

    private static bool IsDisableForUnsafeHttpMethodsInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "DisableForUnsafeHttpMethods"
            })
        {
            return false;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return IsFrameworkUnsafeMethodRetryGuard(method);
        }

        var candidateMethods = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
        return candidateMethods.Length == 0 || candidateMethods.All(IsFrameworkUnsafeMethodRetryGuard);
    }

    private static bool IsFrameworkUnsafeMethodRetryGuard(IMethodSymbol method)
    {
        var containingNamespace = (method.ReducedFrom ?? method).ContainingNamespace;
        return containingNamespace.IsGlobalNamespace ||
            containingNamespace.ToDisplayString() == "Microsoft.Extensions.Http.Resilience";
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

    private static TypedClientRegistration? FindTypedClientImplementationInChain(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        ExpressionSyntax current = invocation;

        while (current is InvocationExpressionSyntax currentInvocation)
        {
            if (currentInvocation.Expression is MemberAccessExpressionSyntax
                {
                    Name: GenericNameSyntax
                    {
                        Identifier.ValueText: "AddHttpClient",
                        TypeArgumentList.Arguments.Count: >= 1 and <= 2
                    } genericName
                } addHttpClientAccess &&
                IsServiceCollectionReceiver(addHttpClientAccess.Expression, semanticModel, cancellationToken))
            {
                var implementationTypeIndex = genericName.TypeArgumentList.Arguments.Count == 2 ? 1 : 0;
                return CreateTypedClientRegistration(
                    genericName.TypeArgumentList.Arguments[implementationTypeIndex],
                    semanticModel,
                    cancellationToken);
            }

            if (currentInvocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                break;
            }

            current = memberAccess.Expression;
        }

        return null;
    }

    private static TypedClientRegistration? FindTypedClientImplementationForBuilderLocal(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return FindAddHttpClientInvocationForBuilderLocal(invocation) is { } addHttpClient
            ? FindTypedClientImplementationInChain(addHttpClient, semanticModel, cancellationToken)
            : null;
    }

    private static TypedClientRegistration CreateTypedClientRegistration(
        TypeSyntax implementationType,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var typeSymbol = semanticModel.GetTypeInfo(implementationType, cancellationToken).Type;
        return new TypedClientRegistration(
            implementationType.ToString(),
            typeSymbol is not null and not IErrorTypeSymbol
                ? NormalizeTypeName(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                : null);
    }

    private static string? FindNamedClientInChain(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        ExpressionSyntax current = invocation;

        while (current is InvocationExpressionSyntax currentInvocation)
        {
            if (currentInvocation.Expression is MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: "AddHttpClient"
                } addHttpClientAccess &&
                IsServiceCollectionReceiver(addHttpClientAccess.Expression, semanticModel, cancellationToken) &&
                currentInvocation.ArgumentList.Arguments.Count > 0 &&
                TryGetStringConstant(
                    currentInvocation.ArgumentList.Arguments[0].Expression,
                    semanticModel,
                    cancellationToken) is { } clientName)
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

    private static string? FindNamedClientForBuilderLocal(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return FindAddHttpClientInvocationForBuilderLocal(invocation) is { } addHttpClient
            ? FindNamedClientInChain(addHttpClient, semanticModel, cancellationToken)
            : null;
    }

    private static InvocationExpressionSyntax? FindAddHttpClientInvocationForBuilderLocal(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax builderIdentifier
            } ||
            invocation.FirstAncestorOrSelf<BlockSyntax>() is not { } block)
        {
            return null;
        }

        return block
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(variable => variable.Identifier.ValueText == builderIdentifier.Identifier.ValueText &&
                variable.SpanStart < invocation.SpanStart &&
                variable.Initializer is not null &&
                !LocalIsReassignedBetween(
                    block,
                    builderIdentifier.Identifier.ValueText,
                    variable.SpanStart,
                    invocation.SpanStart))
            .Select(variable => UnwrapParentheses(variable.Initializer!.Value))
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();
    }

    private static bool IsServiceCollectionReceiver(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return IsServiceCollectionType(semanticModel.GetTypeInfo(expression, cancellationToken).Type) ||
            semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol switch
            {
                ILocalSymbol local => IsServiceCollectionType(local.Type) || SyntacticDeclarationLooksLikeServiceCollection(local),
                IParameterSymbol parameter => IsServiceCollectionType(parameter.Type) || SyntacticDeclarationLooksLikeServiceCollection(parameter),
                IFieldSymbol field => IsServiceCollectionType(field.Type) || SyntacticDeclarationLooksLikeServiceCollection(field),
                IPropertySymbol property => IsServiceCollectionType(property.Type) || SyntacticDeclarationLooksLikeServiceCollection(property),
                _ => false
            } ||
            SyntacticReceiverLooksLikeServiceCollection(expression);
    }

    private static bool IsServiceCollectionType(ITypeSymbol? type)
    {
        return type is not null &&
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::Microsoft.Extensions.DependencyInjection.IServiceCollection";
    }

    private static bool SyntacticDeclarationLooksLikeServiceCollection(ISymbol symbol)
    {
        return symbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .Any(syntax => syntax switch
            {
                ParameterSyntax parameter => parameter.Type is not null &&
                    IsServiceCollectionTypeName(parameter.Type),
                VariableDeclaratorSyntax variable => variable.Parent is VariableDeclarationSyntax declaration &&
                    IsServiceCollectionTypeName(declaration.Type),
                PropertyDeclarationSyntax property => IsServiceCollectionTypeName(property.Type),
                _ => false
            });
    }

    private static bool SyntacticReceiverLooksLikeServiceCollection(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText is "services" or "serviceCollection" &&
                (ParameterLooksLikeServiceCollection(identifier) ||
                    LocalLooksLikeServiceCollection(identifier) ||
                    FieldOrPropertyLooksLikeServiceCollection(identifier)),
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Services" } => false,
            _ => false
        };
    }

    private static bool ParameterLooksLikeServiceCollection(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>()?
            .ParameterList.Parameters
            .Any(parameter => parameter.Identifier.ValueText == identifier.Identifier.ValueText &&
                parameter.Type is not null &&
                IsServiceCollectionTypeName(parameter.Type)) == true;
    }

    private static bool LocalLooksLikeServiceCollection(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<BlockSyntax>()?
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText &&
                variable.Parent is VariableDeclarationSyntax declaration &&
                IsServiceCollectionTypeName(declaration.Type)) == true;
    }

    private static bool FieldOrPropertyLooksLikeServiceCollection(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<TypeDeclarationSyntax>()?
            .Members
            .Any(member => member switch
            {
                FieldDeclarationSyntax field => IsServiceCollectionTypeName(field.Declaration.Type) &&
                    field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText),
                PropertyDeclarationSyntax property => IsServiceCollectionTypeName(property.Type) &&
                    property.Identifier.ValueText == identifier.Identifier.ValueText,
                _ => false
            }) == true;
    }

    private static bool IsServiceCollectionTypeName(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "IServiceCollection",
            QualifiedNameSyntax qualified => qualified.ToString() == "Microsoft.Extensions.DependencyInjection.IServiceCollection" ||
                qualified.ToString() == "global::Microsoft.Extensions.DependencyInjection.IServiceCollection",
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.ToString() == "global::Microsoft.Extensions.DependencyInjection.IServiceCollection",
            _ => false
        };
    }

    private static bool TypedClientSendsUnsafeHttpMethod(IEnumerable<SyntaxNode> roots, TypedClientRegistration typedClient)
    {
        return roots
            .SelectMany(root => root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(type => DeclaredTypeMatchesRegistration(type, typedClient))
            .SelectMany(type => type.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Any(invocation => IsUnsafeHttpClientCall(invocation, roots));
    }

    private static bool DeclaredTypeMatchesRegistration(
        ClassDeclarationSyntax classDeclaration,
        TypedClientRegistration registration)
    {
        if (registration.ResolvedTypeName is not null)
        {
            return GetQualifiedClassName(classDeclaration) == registration.ResolvedTypeName;
        }

        var registrationTypeName = NormalizeTypeName(registration.RawTypeName);
        if (registrationTypeName.Contains("."))
        {
            return GetQualifiedClassName(classDeclaration) == registrationTypeName;
        }

        return classDeclaration.Identifier.ValueText == TypeNameUtilities.ToSimpleName(registrationTypeName);
    }

    private static string NormalizeTypeName(string registrationTypeName)
    {
        registrationTypeName = registrationTypeName.Trim();
        if (registrationTypeName.StartsWith("global::", System.StringComparison.Ordinal))
        {
            registrationTypeName = registrationTypeName.Substring("global::".Length);
        }

        return registrationTypeName;
    }

    private static string GetQualifiedClassName(ClassDeclarationSyntax classDeclaration)
    {
        var namespaceName = string.Join(
            ".",
            classDeclaration
                .Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse()
                .Select(ns => ns.Name.ToString()));

        return string.IsNullOrEmpty(namespaceName)
            ? classDeclaration.Identifier.ValueText
            : namespaceName + "." + classDeclaration.Identifier.ValueText;
    }

    private static bool NamedClientSendsUnsafeHttpMethod(
        IEnumerable<SyntaxNode> roots,
        string clientName)
    {
        foreach (var invocation in roots
            .SelectMany(root => root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Where(invocation => IsCreateClientInvocation(invocation, roots, clientName)))
        {
            if (IsDirectUnsafeCall(invocation, roots) ||
                AssignedClientSendsUnsafeHttpMethod(invocation, roots))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnsafeHttpCall(InvocationExpressionSyntax invocation, IEnumerable<SyntaxNode> roots)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        return IsUnsafeHttpCall(memberAccess.Name.Identifier.ValueText, invocation, roots);
    }

    private static bool IsUnsafeHttpClientCall(
        InvocationExpressionSyntax invocation,
        IEnumerable<SyntaxNode> roots)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            !SyntacticReceiverLooksLikeHttpClient(memberAccess.Expression))
        {
            return false;
        }

        return IsUnsafeHttpCall(memberAccess.Name.Identifier.ValueText, invocation, roots);
    }

    private static bool IsUnsafeHttpCall(
        string methodName,
        InvocationExpressionSyntax invocation,
        IEnumerable<SyntaxNode> roots)
    {
        return UnsafeHttpMethodPrefixes.Any(prefix => methodName.StartsWith(prefix, System.StringComparison.Ordinal)) ||
            (methodName is "Send" or "SendAsync" &&
                invocation.ArgumentList.Arguments.Count > 0 &&
                RequestExpressionUsesUnsafeHttpMethod(
                    invocation.ArgumentList.Arguments[0].Expression,
                    invocation,
                    roots));
    }

    private static bool SyntacticReceiverLooksLikeHttpClient(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => ParameterLooksLikeHttpClient(identifier) ||
                LocalLooksLikeHttpClient(identifier) ||
                FieldOrPropertyLooksLikeHttpClient(identifier),
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax name } =>
                FieldOrPropertyLooksLikeHttpClient(name),
            _ => false
        };
    }

    private static bool ParameterLooksLikeHttpClient(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>()?
            .ParameterList.Parameters
            .Any(parameter => parameter.Identifier.ValueText == identifier.Identifier.ValueText &&
                parameter.Type is not null &&
                IsHttpClientTypeName(parameter.Type)) == true ||
            identifier.FirstAncestorOrSelf<ClassDeclarationSyntax>()?
                .ParameterList?.Parameters
                .Any(parameter => parameter.Identifier.ValueText == identifier.Identifier.ValueText &&
                    parameter.Type is not null &&
                    IsHttpClientTypeName(parameter.Type)) == true;
    }

    private static bool LocalLooksLikeHttpClient(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<BlockSyntax>()?
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText &&
                variable.Parent is VariableDeclarationSyntax declaration &&
                IsHttpClientTypeName(declaration.Type)) == true;
    }

    private static bool FieldOrPropertyLooksLikeHttpClient(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<TypeDeclarationSyntax>()?
            .Members
            .Any(member => member switch
            {
                FieldDeclarationSyntax field => IsHttpClientTypeName(field.Declaration.Type) &&
                    field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText),
                PropertyDeclarationSyntax property => IsHttpClientTypeName(property.Type) &&
                    property.Identifier.ValueText == identifier.Identifier.ValueText,
                _ => false
            }) == true;
    }

    private static bool IsHttpClientTypeName(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "HttpClient",
            QualifiedNameSyntax qualified => qualified.ToString() == "System.Net.Http.HttpClient" ||
                qualified.ToString() == "global::System.Net.Http.HttpClient",
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.ToString() == "global::System.Net.Http.HttpClient",
            _ => false
        };
    }

    private static bool RequestExpressionUsesUnsafeHttpMethod(
        ExpressionSyntax expression,
        SyntaxNode context,
        IEnumerable<SyntaxNode> roots)
    {
        expression = UnwrapParentheses(expression);

        return expression switch
        {
            ObjectCreationExpressionSyntax objectCreation => HttpRequestCreationUsesUnsafeMethod(objectCreation, roots),
            ImplicitObjectCreationExpressionSyntax implicitObjectCreation =>
                HttpRequestCreationUsesUnsafeMethod(implicitObjectCreation, roots),
            IdentifierNameSyntax identifier => LocalRequestVariableUsesUnsafeMethod(identifier, context, roots),
            _ => false
        };
    }

    private static bool HttpRequestCreationUsesUnsafeMethod(
        BaseObjectCreationExpressionSyntax objectCreation,
        IEnumerable<SyntaxNode> roots)
    {
        return objectCreation.ArgumentList?.Arguments
            .Select(argument => UnwrapParentheses(argument.Expression))
            .Any(expression => IsUnsafeHttpMethodExpression(expression, roots)) == true ||
            objectCreation.Initializer?.Expressions
                .OfType<AssignmentExpressionSyntax>()
                .Any(assignment => IsMethodMember(assignment.Left) &&
                    IsUnsafeHttpMethodExpression(UnwrapParentheses(assignment.Right), roots)) == true;
    }

    private static bool LocalRequestVariableUsesUnsafeMethod(
        IdentifierNameSyntax identifier,
        SyntaxNode context,
        IEnumerable<SyntaxNode> roots)
    {
        var containingBlock = context.FirstAncestorOrSelf<BlockSyntax>();
        if (containingBlock is null)
        {
            return false;
        }

        return containingBlock
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText &&
                variable.SpanStart < context.SpanStart &&
                variable.Initializer is not null &&
                !LocalIsReassignedBetween(
                    containingBlock,
                    identifier.Identifier.ValueText,
                    variable.SpanStart,
                    context.SpanStart) &&
                RequestExpressionUsesUnsafeHttpMethod(variable.Initializer!.Value, variable, roots));
    }

    private static bool IsMethodMember(ExpressionSyntax expression)
    {
        expression = UnwrapParentheses(expression);

        return expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "Method",
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText == "Method",
            _ => false
        };
    }

    private static bool IsUnsafeHttpMethodExpression(ExpressionSyntax expression, IEnumerable<SyntaxNode> roots)
    {
        expression = UnwrapParentheses(expression);

        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess when
                memberAccess.Expression.ToString() == "HttpMethod" =>
                UnsafeHttpMethodNames.Contains(memberAccess.Name.Identifier.ValueText, System.StringComparer.Ordinal),
            ObjectCreationExpressionSyntax objectCreation when objectCreation.Type.ToString() == "HttpMethod" =>
                objectCreation.ArgumentList?.Arguments.Count > 0 &&
                TryGetStringConstant(objectCreation.ArgumentList.Arguments[0].Expression, roots) is { } method &&
                UnsafeHttpMethodNames.Contains(method, System.StringComparer.OrdinalIgnoreCase),
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

    private static bool IsCreateClientInvocation(
        InvocationExpressionSyntax invocation,
        IEnumerable<SyntaxNode> roots,
        string clientName)
    {
        return invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
        memberAccess.Name.Identifier.ValueText == "CreateClient" &&
        SyntacticReceiverLooksLikeHttpClientFactory(memberAccess.Expression) &&
        invocation.ArgumentList.Arguments.Count > 0 &&
        TryGetStringConstant(
            invocation.ArgumentList.Arguments[0].Expression,
            roots) == clientName;
    }

    private static bool SyntacticReceiverLooksLikeHttpClientFactory(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => ParameterLooksLikeHttpClientFactory(identifier) ||
                LocalLooksLikeHttpClientFactory(identifier) ||
                FieldOrPropertyLooksLikeHttpClientFactory(identifier),
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax name } =>
                FieldOrPropertyLooksLikeHttpClientFactory(name),
            _ => false
        };
    }

    private static bool ParameterLooksLikeHttpClientFactory(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>()?
            .ParameterList.Parameters
            .Any(parameter => parameter.Identifier.ValueText == identifier.Identifier.ValueText &&
                parameter.Type is not null &&
                IsHttpClientFactoryTypeName(parameter.Type)) == true ||
            identifier.FirstAncestorOrSelf<ClassDeclarationSyntax>()?
                .ParameterList?.Parameters
                .Any(parameter => parameter.Identifier.ValueText == identifier.Identifier.ValueText &&
                    parameter.Type is not null &&
                    IsHttpClientFactoryTypeName(parameter.Type)) == true;
    }

    private static bool LocalLooksLikeHttpClientFactory(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<BlockSyntax>()?
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText &&
                variable.Parent is VariableDeclarationSyntax declaration &&
                IsHttpClientFactoryTypeName(declaration.Type)) == true;
    }

    private static bool FieldOrPropertyLooksLikeHttpClientFactory(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<TypeDeclarationSyntax>()?
            .Members
            .Any(member => member switch
            {
                FieldDeclarationSyntax field => IsHttpClientFactoryTypeName(field.Declaration.Type) &&
                    field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText),
                PropertyDeclarationSyntax property => IsHttpClientFactoryTypeName(property.Type) &&
                    property.Identifier.ValueText == identifier.Identifier.ValueText,
                _ => false
            }) == true;
    }

    private static bool IsHttpClientFactoryTypeName(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "IHttpClientFactory",
            QualifiedNameSyntax qualified => qualified.ToString() == "System.Net.Http.IHttpClientFactory" ||
                qualified.ToString() == "global::System.Net.Http.IHttpClientFactory",
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.ToString() == "global::System.Net.Http.IHttpClientFactory",
            _ => false
        };
    }

    private static bool IsDirectUnsafeCall(
        InvocationExpressionSyntax createClientInvocation,
        IEnumerable<SyntaxNode> roots)
    {
        return createClientInvocation.Parent is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Parent is InvocationExpressionSyntax invocation &&
            IsUnsafeHttpCall(invocation, roots);
    }

    private static bool AssignedClientSendsUnsafeHttpMethod(
        InvocationExpressionSyntax createClientInvocation,
        IEnumerable<SyntaxNode> roots)
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
            } &&
                identifier.Identifier.ValueText == localName &&
                invocation.SpanStart > declarator.SpanStart &&
                !LocalIsReassignedBetween(containingBlock, localName, declarator.SpanStart, invocation.SpanStart) &&
                IsUnsafeHttpCall(invocation, roots));
    }

    private static string? TryGetStringLiteral(ExpressionSyntax expression)
    {
        return expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;
    }

    private static string? TryGetStringConstant(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (TryGetStringLiteral(expression) is { } literal)
        {
            return literal;
        }

        var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
        return constantValue.HasValue && constantValue.Value is string value
            ? value
            : null;
    }

    private static string? TryGetStringConstant(ExpressionSyntax expression, IEnumerable<SyntaxNode> roots)
    {
        expression = UnwrapParentheses(expression);

        if (TryGetStringLiteral(expression) is { } literal)
        {
            return literal;
        }

        return expression switch
        {
            IdentifierNameSyntax identifier => TryGetLocalStringConstant(identifier) ??
                TryGetFieldStringConstant(roots, identifier.Identifier.ValueText, typeName: null),
            MemberAccessExpressionSyntax memberAccess => TryGetFieldStringConstant(
                roots,
                memberAccess.Name.Identifier.ValueText,
                TypeNameUtilities.ToSimpleName(memberAccess.Expression.ToString())),
            _ => null
        };
    }

    private static string? TryGetLocalStringConstant(IdentifierNameSyntax identifier)
    {
        return identifier
            .Ancestors()
            .OfType<BlockSyntax>()
            .SelectMany(block => block
                .DescendantNodes()
                .OfType<LocalDeclarationStatementSyntax>())
            .Where(localDeclaration => localDeclaration.SpanStart < identifier.SpanStart &&
                localDeclaration.Modifiers.Any(SyntaxKind.ConstKeyword) &&
                IsStringTypeName(localDeclaration.Declaration.Type))
            .SelectMany(localDeclaration => localDeclaration.Declaration.Variables)
            .Where(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText)
            .Select(variable => variable.Initializer?.Value)
            .OfType<ExpressionSyntax>()
            .Select(TryGetStringLiteral)
            .FirstOrDefault(value => value is not null);
    }

    private static string? TryGetFieldStringConstant(
        IEnumerable<SyntaxNode> roots,
        string constantName,
        string? typeName)
    {
        return roots
            .SelectMany(root => root.DescendantNodes().OfType<FieldDeclarationSyntax>())
            .Where(field => field.Modifiers.Any(SyntaxKind.ConstKeyword) &&
                IsStringTypeName(field.Declaration.Type) &&
                (typeName is null ||
                    field.Parent is TypeDeclarationSyntax typeDeclaration &&
                    typeDeclaration.Identifier.ValueText == typeName))
            .SelectMany(field => field.Declaration.Variables)
            .Where(variable => variable.Identifier.ValueText == constantName)
            .Select(variable => variable.Initializer?.Value)
            .OfType<ExpressionSyntax>()
            .Select(TryGetStringLiteral)
            .FirstOrDefault(value => value is not null);
    }

    private static bool IsStringTypeName(TypeSyntax type)
    {
        return type switch
        {
            PredefinedTypeSyntax predefined => predefined.Keyword.IsKind(SyntaxKind.StringKeyword),
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "String",
            QualifiedNameSyntax qualified => qualified.ToString() == "System.String" ||
                qualified.ToString() == "global::System.String",
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.ToString() == "global::System.String",
            _ => false
        };
    }

    private static bool LocalIsReassignedBetween(
        BlockSyntax containingBlock,
        string localName,
        int start,
        int end)
    {
        return containingBlock
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.SpanStart > start &&
                assignment.SpanStart < end &&
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                assignment.Left is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == localName);
    }
}
