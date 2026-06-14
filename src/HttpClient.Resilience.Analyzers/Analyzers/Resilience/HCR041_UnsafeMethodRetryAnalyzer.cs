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

        var typedClient = FindTypedClientInChain(invocation, context.SemanticModel, context.CancellationToken);

        if (typedClient is not null && TypedClientSendsUnsafeHttpMethod(roots, typedClient))
        {
            ReportDiagnostic(context, invocation);
            return;
        }

        var namedClient = FindNamedClientInChain(invocation, context.SemanticModel, context.CancellationToken);
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

    private static string? FindTypedClientInChain(
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
                        TypeArgumentList.Arguments.Count: 1
                    } genericName
                } addHttpClientAccess &&
                IsServiceCollectionReceiver(addHttpClientAccess.Expression, semanticModel, cancellationToken))
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
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Services" } => true,
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

    private static bool TypedClientSendsUnsafeHttpMethod(IEnumerable<SyntaxNode> roots, string typedClient)
    {
        return roots
            .SelectMany(root => root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(type => DeclaredTypeMatchesRegistration(type, typedClient))
            .SelectMany(type => type.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Any(IsUnsafeHttpClientCall);
    }

    private static bool DeclaredTypeMatchesRegistration(ClassDeclarationSyntax classDeclaration, string registrationTypeName)
    {
        registrationTypeName = registrationTypeName.Trim();
        if (registrationTypeName.StartsWith("global::", System.StringComparison.Ordinal))
        {
            registrationTypeName = registrationTypeName.Substring("global::".Length);
        }

        if (registrationTypeName.Contains("."))
        {
            return GetQualifiedClassName(classDeclaration) == registrationTypeName;
        }

        return classDeclaration.Identifier.ValueText == TypeNameUtilities.ToSimpleName(registrationTypeName);
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

        return IsUnsafeHttpCall(memberAccess.Name.Identifier.ValueText, invocation);
    }

    private static bool IsUnsafeHttpClientCall(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            !SyntacticReceiverLooksLikeHttpClient(memberAccess.Expression))
        {
            return false;
        }

        return IsUnsafeHttpCall(memberAccess.Name.Identifier.ValueText, invocation);
    }

    private static bool IsUnsafeHttpCall(string methodName, InvocationExpressionSyntax invocation)
    {
        return UnsafeHttpMethodPrefixes.Any(prefix => methodName.StartsWith(prefix, System.StringComparison.Ordinal)) ||
            (methodName is "Send" or "SendAsync" &&
                invocation.ArgumentList.Arguments.Count > 0 &&
                RequestExpressionUsesUnsafeHttpMethod(invocation.ArgumentList.Arguments[0].Expression, invocation));
    }

    private static bool SyntacticReceiverLooksLikeHttpClient(ExpressionSyntax expression)
    {
        return expression is IdentifierNameSyntax identifier &&
            (ParameterLooksLikeHttpClient(identifier) ||
                LocalLooksLikeHttpClient(identifier) ||
                FieldOrPropertyLooksLikeHttpClient(identifier));
    }

    private static bool ParameterLooksLikeHttpClient(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>()?
            .ParameterList.Parameters
            .Any(parameter => parameter.Identifier.ValueText == identifier.Identifier.ValueText &&
                parameter.Type is not null &&
                HttpClientSymbols.IsHttpClientName(parameter.Type)) == true ||
            identifier.FirstAncestorOrSelf<ClassDeclarationSyntax>()?
                .ParameterList?.Parameters
                .Any(parameter => parameter.Identifier.ValueText == identifier.Identifier.ValueText &&
                    parameter.Type is not null &&
                    HttpClientSymbols.IsHttpClientName(parameter.Type)) == true;
    }

    private static bool LocalLooksLikeHttpClient(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<BlockSyntax>()?
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText &&
                variable.Parent is VariableDeclarationSyntax declaration &&
                HttpClientSymbols.IsHttpClientName(declaration.Type)) == true;
    }

    private static bool FieldOrPropertyLooksLikeHttpClient(IdentifierNameSyntax identifier)
    {
        return identifier.FirstAncestorOrSelf<TypeDeclarationSyntax>()?
            .Members
            .Any(member => member switch
            {
                FieldDeclarationSyntax field => HttpClientSymbols.IsHttpClientName(field.Declaration.Type) &&
                    field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText),
                PropertyDeclarationSyntax property => HttpClientSymbols.IsHttpClientName(property.Type) &&
                    property.Identifier.ValueText == identifier.Identifier.ValueText,
                _ => false
            }) == true;
    }

    private static bool RequestExpressionUsesUnsafeHttpMethod(ExpressionSyntax expression, SyntaxNode context)
    {
        expression = UnwrapParentheses(expression);

        return expression switch
        {
            ObjectCreationExpressionSyntax objectCreation => HttpRequestCreationUsesUnsafeMethod(objectCreation),
            ImplicitObjectCreationExpressionSyntax implicitObjectCreation => HttpRequestCreationUsesUnsafeMethod(implicitObjectCreation),
            IdentifierNameSyntax identifier => LocalRequestVariableUsesUnsafeMethod(identifier, context),
            _ => false
        };
    }

    private static bool HttpRequestCreationUsesUnsafeMethod(BaseObjectCreationExpressionSyntax objectCreation)
    {
        return objectCreation.ArgumentList?.Arguments
            .Select(argument => UnwrapParentheses(argument.Expression))
            .Any(IsUnsafeHttpMethodExpression) == true ||
            objectCreation.Initializer?.Expressions
                .OfType<AssignmentExpressionSyntax>()
                .Any(assignment => IsMethodMember(assignment.Left) &&
                    IsUnsafeHttpMethodExpression(UnwrapParentheses(assignment.Right))) == true;
    }

    private static bool LocalRequestVariableUsesUnsafeMethod(IdentifierNameSyntax identifier, SyntaxNode context)
    {
        var containingBlock = context.FirstAncestorOrSelf<BlockSyntax>();
        if (containingBlock is null)
        {
            return false;
        }

        return containingBlock
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(variable => variable.Identifier.ValueText == identifier.Identifier.ValueText &&
                variable.SpanStart < context.SpanStart &&
                variable.Initializer is not null)
            .Any(variable => RequestExpressionUsesUnsafeHttpMethod(variable.Initializer!.Value, variable));
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

    private static bool IsUnsafeHttpMethodExpression(ExpressionSyntax expression)
    {
        expression = UnwrapParentheses(expression);

        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess when
                memberAccess.Expression.ToString() == "HttpMethod" =>
                UnsafeHttpMethodNames.Contains(memberAccess.Name.Identifier.ValueText, System.StringComparer.Ordinal),
            ObjectCreationExpressionSyntax objectCreation when objectCreation.Type.ToString() == "HttpMethod" =>
                objectCreation.ArgumentList?.Arguments.Count > 0 &&
                TryGetStringLiteral(objectCreation.ArgumentList.Arguments[0].Expression) is { } method &&
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
