using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.Handlers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR020_DelegatingHandlerCapturesScopedDataAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] RequestScopedTypeNames =
    {
        "IHttpContextAccessor",
        "HttpContext",
        "ClaimsPrincipal",
        "ISession"
    };

    private static readonly string[] QualifiedRequestScopedTypeNames =
    {
        "Microsoft.AspNetCore.Http.IHttpContextAccessor",
        "Microsoft.AspNetCore.Http.HttpContext",
        "Microsoft.AspNetCore.Http.ISession",
        "System.Security.Claims.ClaimsPrincipal"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR020);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationStartAnalysisContext context)
    {
        var roots = CompilationSyntaxIndex.GetRoots(context.Compilation, context.CancellationToken);
        var scopedTypes = GetKnownScopedTypes(
            context.Compilation,
            context.CancellationToken);
        var handlerTypes = GetKnownDelegatingHandlerTypes(roots);

        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeClass(nodeContext, scopedTypes, handlerTypes),
            SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeClass(
        SyntaxNodeAnalysisContext context,
        ScopedTypeNames scopedTypes,
        ISet<string> handlerTypes)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        if (!DerivesFromDelegatingHandler(
                classDeclaration,
                handlerTypes,
                context.SemanticModel,
                context.CancellationToken))
        {
            return;
        }

        var reportedConstructorParameter = false;
        foreach (var parameter in GetConstructorParameters(classDeclaration))
        {
            if (parameter.Type is null ||
                !IsRequestScopedType(
                    parameter.Type,
                    scopedTypes,
                    context.SemanticModel,
                    context.CancellationToken))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.HCR020,
                parameter.Type.GetLocation()));
            reportedConstructorParameter = true;
        }

        if (reportedConstructorParameter)
        {
            return;
        }

        foreach (var field in classDeclaration.Members.OfType<FieldDeclarationSyntax>())
        {
            if (!IsRequestScopedType(
                    field.Declaration.Type,
                    scopedTypes,
                    context.SemanticModel,
                    context.CancellationToken))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.HCR020,
                field.Declaration.Type.GetLocation()));
        }

        foreach (var property in classDeclaration.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (!IsRequestScopedType(
                    property.Type,
                    scopedTypes,
                    context.SemanticModel,
                    context.CancellationToken))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.HCR020,
                property.Type.GetLocation()));
        }
    }

    private static bool DerivesFromDelegatingHandler(
        ClassDeclarationSyntax classDeclaration,
        ISet<string> handlerTypes,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return classDeclaration.BaseList?.Types.Any(type =>
            IsDelegatingHandlerBaseType(
                type.Type,
                handlerTypes,
                semanticModel,
                cancellationToken)) == true;
    }

    private static bool IsDelegatingHandlerBaseType(
        TypeSyntax type,
        ISet<string> handlerTypes,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var resolvedType = semanticModel.GetTypeInfo(type, cancellationToken).Type;
        if (resolvedType is not null && resolvedType is not IErrorTypeSymbol)
        {
            return IsDelegatingHandlerSymbol(resolvedType) ||
                handlerTypes.Contains(NormalizeTypeName(resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }

        return IsDelegatingHandlerTypeName(type) ||
            IsKnownDelegatingHandlerType(type, handlerTypes);
    }

    private static bool IsDelegatingHandlerSymbol(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::System.Net.Http.DelegatingHandler";
    }

    private static bool IsDelegatingHandlerTypeName(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "DelegatingHandler",
            QualifiedNameSyntax qualified => qualified.ToString() == "System.Net.Http.DelegatingHandler" ||
                qualified.ToString() == "global::System.Net.Http.DelegatingHandler",
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.ToString() == "global::System.Net.Http.DelegatingHandler",
            _ => false
        };
    }

    private static bool IsKnownDelegatingHandlerType(TypeSyntax type, ISet<string> handlerTypes)
    {
        var typeName = type.ToString();

        return TypeIsQualified(type)
            ? handlerTypes.Contains(NormalizeTypeName(typeName))
            : handlerTypes.Contains(TypeNameUtilities.ToSimpleName(typeName));
    }

    private static ISet<string> GetKnownDelegatingHandlerTypes(IEnumerable<SyntaxNode> roots)
    {
        var classes = roots
            .SelectMany(root => root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .ToArray();
        var handlerTypes = new HashSet<string>(System.StringComparer.Ordinal);
        var changed = true;

        while (changed)
        {
            changed = false;

            foreach (var classDeclaration in classes)
            {
                if (classDeclaration.BaseList?.Types.Any(type =>
                    IsDelegatingHandlerTypeName(type.Type) ||
                    IsKnownDelegatingHandlerType(type.Type, handlerTypes)) != true)
                {
                    continue;
                }

                foreach (var typeName in TypeNameUtilities.GetComparableNames(GetQualifiedClassName(classDeclaration)))
                {
                    changed |= handlerTypes.Add(typeName);
                }
            }
        }

        return handlerTypes;
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

    private sealed class ScopedTypeNames
    {
        // Verbatim registration names plus resolved fully-qualified names; consulted
        // only when the consumer's type resolves semantically.
        public readonly ISet<string> Resolved = new HashSet<string>(System.StringComparer.Ordinal);

        // Raw and simple registration names; consulted only for unresolved syntax.
        public readonly ISet<string> Comparable = new HashSet<string>(System.StringComparer.Ordinal);
    }

    private static ScopedTypeNames GetKnownScopedTypes(
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var scopedTypes = new ScopedTypeNames();

        foreach (var registration in ServiceRegistrationCollector
            .CollectFrameworkRegistrations(compilation, cancellationToken)
            .Where(registration => registration.Kind == ServiceRegistrationKind.Scoped))
        {
            foreach (var typeName in new[]
            {
                registration.ServiceTypeName,
                registration.ImplementationTypeName
            })
            {
                if (typeName is null)
                {
                    continue;
                }

                scopedTypes.Resolved.Add(typeName);
                foreach (var comparableName in TypeNameUtilities.GetComparableNames(typeName))
                {
                    scopedTypes.Comparable.Add(comparableName);
                }
            }

            // Raw syntax names only match consumers that spell the type the same way;
            // resolve the registration's type syntax so namespaced scoped services are
            // recognized under their fully-qualified name.
            var registrationModel = GetSemanticModel(compilation, registration.Invocation.SyntaxTree);
            foreach (var typeSyntax in GetRegistrationTypeSyntaxes(registration.Invocation))
            {
                var resolvedType = registrationModel.GetTypeInfo(typeSyntax, cancellationToken).Type;
                if (resolvedType is null || resolvedType is IErrorTypeSymbol)
                {
                    continue;
                }

                scopedTypes.Resolved.Add(NormalizeTypeName(
                    resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            }
        }

        return scopedTypes;
    }

    private static IEnumerable<TypeSyntax> GetRegistrationTypeSyntaxes(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax
            {
                Name: GenericNameSyntax genericName
            })
        {
            foreach (var typeArgument in genericName.TypeArgumentList.Arguments)
            {
                yield return typeArgument;
            }
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            foreach (var typeSyntax in GetArgumentTypeSyntaxes(argument.Expression))
            {
                yield return typeSyntax;
            }
        }
    }

    private static IEnumerable<TypeSyntax> GetArgumentTypeSyntaxes(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case TypeOfExpressionSyntax typeOfExpression:
                yield return typeOfExpression.Type;
                yield break;
            case ObjectCreationExpressionSyntax objectCreation:
                yield return objectCreation.Type;
                yield break;
            case LambdaExpressionSyntax { Body: ExpressionSyntax body }:
                foreach (var typeSyntax in GetArgumentTypeSyntaxes(body))
                {
                    yield return typeSyntax;
                }

                yield break;
            case LambdaExpressionSyntax { Body: BlockSyntax block }:
                foreach (var returnExpression in block.Statements
                    .OfType<ReturnStatementSyntax>()
                    .Select(returnStatement => returnStatement.Expression)
                    .OfType<ExpressionSyntax>())
                {
                    foreach (var typeSyntax in GetArgumentTypeSyntaxes(returnExpression))
                    {
                        yield return typeSyntax;
                    }
                }

                yield break;
            case ParenthesizedExpressionSyntax parenthesized:
                foreach (var typeSyntax in GetArgumentTypeSyntaxes(parenthesized.Expression))
                {
                    yield return typeSyntax;
                }

                yield break;
            default:
                yield break;
        }
    }

#pragma warning disable RS1030 // HCR020 performs compilation-wide scoped-service matching and needs cross-tree semantic type checks.
    private static SemanticModel GetSemanticModel(Compilation compilation, SyntaxTree syntaxTree)
    {
        return compilation.GetSemanticModel(syntaxTree);
    }
#pragma warning restore RS1030

    private static IEnumerable<ParameterSyntax> GetConstructorParameters(ClassDeclarationSyntax classDeclaration)
    {
        foreach (var constructor in classDeclaration.Members.OfType<ConstructorDeclarationSyntax>())
        {
            foreach (var parameter in constructor.ParameterList.Parameters)
            {
                yield return parameter;
            }
        }

        if (classDeclaration.ParameterList is null)
        {
            yield break;
        }

        foreach (var parameter in classDeclaration.ParameterList.Parameters)
        {
            yield return parameter;
        }
    }

    private static bool IsRequestScopedType(
        TypeSyntax type,
        ScopedTypeNames scopedTypes,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        type = UnwrapNullableType(type);

        if (TryGetRequestScopedWrapperArgument(type, out var wrappedType))
        {
            return IsRequestScopedType(wrappedType, scopedTypes, semanticModel, cancellationToken);
        }

        var resolvedType = semanticModel.GetTypeInfo(type, cancellationToken).Type;
        if (resolvedType is not null && resolvedType is not IErrorTypeSymbol)
        {
            return IsRequestScopedType(resolvedType, scopedTypes);
        }

        if (TypeIsQualified(type))
        {
            var qualifiedTypeName = NormalizeTypeName(type.ToString());
            return QualifiedRequestScopedTypeNames.Contains(qualifiedTypeName, System.StringComparer.Ordinal) ||
                scopedTypes.Comparable.Contains(qualifiedTypeName);
        }

        var simpleTypeName = GetSimpleTypeName(type);
        if (RequestScopedTypeNames.Contains(simpleTypeName, System.StringComparer.Ordinal))
        {
            return true;
        }

        return TypeNameUtilities.GetComparableNames(simpleTypeName)
            .Any(scopedTypes.Comparable.Contains);
    }

    private static bool IsRequestScopedType(ITypeSymbol type, ScopedTypeNames scopedTypes)
    {
        var qualifiedTypeName = NormalizeTypeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        if (QualifiedRequestScopedTypeNames.Contains(qualifiedTypeName, System.StringComparer.Ordinal) ||
            scopedTypes.Resolved.Contains(qualifiedTypeName))
        {
            return true;
        }

        if (!type.ContainingNamespace.IsGlobalNamespace)
        {
            return false;
        }

        return RequestScopedTypeNames.Contains(type.Name, System.StringComparer.Ordinal) ||
            scopedTypes.Resolved.Contains(type.Name);
    }

    private static bool TryGetRequestScopedWrapperArgument(TypeSyntax type, out TypeSyntax wrappedType)
    {
        switch (type)
        {
            case GenericNameSyntax genericName when IsRequestScopedWrapperName(genericName.Identifier.ValueText) &&
                genericName.TypeArgumentList.Arguments.Count == 1:
                wrappedType = genericName.TypeArgumentList.Arguments[0];
                return true;
            case QualifiedNameSyntax { Right: GenericNameSyntax genericName } qualified when
                IsQualifiedRequestScopedWrapperName(qualified.Left.ToString(), genericName.Identifier.ValueText) &&
                genericName.TypeArgumentList.Arguments.Count == 1:
                wrappedType = genericName.TypeArgumentList.Arguments[0];
                return true;
            case AliasQualifiedNameSyntax { Alias.Identifier.ValueText: "global", Name: GenericNameSyntax genericName } aliasQualified when
                IsQualifiedRequestScopedWrapperName("global::" + aliasQualified.Name.Identifier.ValueText, genericName.Identifier.ValueText) &&
                genericName.TypeArgumentList.Arguments.Count == 1:
                wrappedType = genericName.TypeArgumentList.Arguments[0];
                return true;
            default:
                wrappedType = type;
                return false;
        }
    }

    private static bool IsRequestScopedWrapperName(string typeName)
    {
        return typeName is
            "Func" or
            "Lazy" or
            "IEnumerable" or
            "IReadOnlyCollection" or
            "IReadOnlyList";
    }

    private static bool IsQualifiedRequestScopedWrapperName(string qualifier, string typeName)
    {
        qualifier = NormalizeTypeName(qualifier);

        return typeName switch
        {
            "Func" or "Lazy" => qualifier == "System",
            "IEnumerable" or "IReadOnlyCollection" or "IReadOnlyList" =>
                qualifier == "System.Collections.Generic",
            _ => false
        };
    }

    private static TypeSyntax UnwrapNullableType(TypeSyntax type)
    {
        return type is NullableTypeSyntax nullable
            ? nullable.ElementType
            : type;
    }

    private static string GetSimpleTypeName(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText,
            _ => type.ToString()
        };
    }

    private static bool TypeIsQualified(TypeSyntax type)
    {
        return type is QualifiedNameSyntax or AliasQualifiedNameSyntax;
    }

    private static string NormalizeTypeName(string typeName)
    {
        typeName = typeName.Trim();
        return typeName.StartsWith("global::", System.StringComparison.Ordinal)
            ? typeName.Substring("global::".Length)
            : typeName;
    }
}
