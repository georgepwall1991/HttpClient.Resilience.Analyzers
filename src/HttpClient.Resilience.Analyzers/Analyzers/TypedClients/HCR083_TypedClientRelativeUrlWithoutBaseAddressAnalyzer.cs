using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using HttpClient.Resilience.Analyzers.Diagnostics;
using HttpClient.Resilience.Analyzers.KnownSymbols;
using HttpClient.Resilience.Analyzers.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HttpClient.Resilience.Analyzers.Analyzers.TypedClients;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HCR083_TypedClientRelativeUrlWithoutBaseAddressAnalyzer : DiagnosticAnalyzer
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

    private static readonly string[] RelativeUrlHttpMethodNames =
    {
        "DeleteAsync",
        "GetAsync",
        "GetByteArrayAsync",
        "GetStreamAsync",
        "GetStringAsync",
        "PatchAsync",
        "PostAsync",
        "PutAsync"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.HCR083);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var roots = context.Compilation.SyntaxTrees
            .Select(tree => tree.GetRoot(context.CancellationToken))
            .ToArray();
        var typedClients = roots
            .SelectMany(root => ServiceRegistrationCollector.CollectFrameworkRegistrations(
                root,
                GetSemanticModel(context.Compilation, root.SyntaxTree),
                context.CancellationToken))
            .Where(registration => registration.Kind == ServiceRegistrationKind.HttpClient &&
                !RegistrationConfiguresBaseAddress(
                    registration,
                    context.Compilation,
                    context.CancellationToken))
            .Select(registration => CreateTypedClientRegistration(
                registration,
                context.Compilation,
                context.CancellationToken))
            .OfType<TypedClientRegistration>()
            .ToArray();

        if (typedClients.Length == 0)
        {
            return;
        }

        foreach (var classDeclaration in roots.SelectMany(root => root.DescendantNodes().OfType<ClassDeclarationSyntax>()))
        {
            if (!typedClients.Any(typedClient => DeclaredTypeMatchesRegistration(classDeclaration, typedClient)))
            {
                continue;
            }

            var semanticModel = GetSemanticModel(context.Compilation, classDeclaration.SyntaxTree);
            foreach (var invocation in classDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (TryGetRelativeHttpClientUrlArgument(invocation, semanticModel, context.CancellationToken, out var urlExpression))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.HCR083,
                        urlExpression.GetLocation()));
                }
            }
        }
    }

    private static TypedClientRegistration? CreateTypedClientRegistration(
        ServiceRegistrationModel registration,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        if (registration.Invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name: GenericNameSyntax genericName
            } ||
            genericName.TypeArgumentList.Arguments.Count is < 1 or > 2)
        {
            return null;
        }

        var implementationType = genericName.TypeArgumentList.Arguments.Count == 2
            ? genericName.TypeArgumentList.Arguments[1]
            : genericName.TypeArgumentList.Arguments[0];
        var semanticModel = GetSemanticModel(compilation, implementationType.SyntaxTree);
        var resolvedType = semanticModel.GetTypeInfo(implementationType, cancellationToken).Type;

        return new TypedClientRegistration(
            implementationType.ToString(),
            resolvedType is not null and not IErrorTypeSymbol
                ? NormalizeTypeName(resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                : null);
    }

    private static bool RegistrationConfiguresBaseAddress(
        ServiceRegistrationModel registration,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var semanticModel = GetSemanticModel(compilation, registration.Invocation.SyntaxTree);
        return InvocationArgumentsConfigureBaseAddress(
                registration.Invocation,
                semanticModel,
                cancellationToken) ||
            ContainingRegistrationStatementConfiguresBaseAddress(
                registration.Invocation,
                compilation,
                cancellationToken) ||
            FollowingBuilderLocalConfiguresBaseAddress(
                registration.Invocation,
                semanticModel,
                cancellationToken);
    }

    private static bool InvocationArgumentsConfigureBaseAddress(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return invocation.ArgumentList.Arguments
            .Select(argument => argument.Expression)
            .Any(expression => ExpressionConfiguresBaseAddress(
                expression,
                semanticModel,
                cancellationToken));
    }

    private static bool ContainingRegistrationStatementConfiguresBaseAddress(
        InvocationExpressionSyntax invocation,
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var statement = invocation.FirstAncestorOrSelf<StatementSyntax>();
        var semanticModel = GetSemanticModel(compilation, invocation.SyntaxTree);
        return statement is not null &&
            statement.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(candidate => candidate.Expression is MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: "ConfigureHttpClient"
                } &&
                    IsFrameworkConfigureHttpClientInvocation(candidate, semanticModel, cancellationToken) &&
                    InvocationArgumentsConfigureBaseAddress(candidate, semanticModel, cancellationToken));
    }

    private static bool IsFrameworkConfigureHttpClientInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return IsFrameworkConfigureHttpClientMethod(method);
        }

        var candidateMethods = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
        return candidateMethods.Length == 0 || candidateMethods.All(IsFrameworkConfigureHttpClientMethod);
    }

    private static bool FollowingBuilderLocalConfiguresBaseAddress(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (invocation.FirstAncestorOrSelf<VariableDeclaratorSyntax>() is not { Initializer: { } initializer } declarator ||
            !initializer.Value.Span.Contains(invocation.Span) ||
            declarator.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>() is not { Parent: BlockSyntax block } declaration)
        {
            return false;
        }

        var localName = declarator.Identifier.ValueText;
        var declarationIndex = block.Statements.IndexOf(declaration);
        for (var index = declarationIndex + 1; index < block.Statements.Count; index++)
        {
            var statement = block.Statements[index];
            if (StatementReassignsLocal(statement, localName))
            {
                return false;
            }

            if (statement.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(candidate => candidate.Expression is MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax receiver,
                    Name.Identifier.ValueText: "ConfigureHttpClient"
                } &&
                    receiver.Identifier.ValueText == localName &&
                    IsFrameworkConfigureHttpClientInvocation(candidate, semanticModel, cancellationToken) &&
                    InvocationArgumentsConfigureBaseAddress(candidate, semanticModel, cancellationToken)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StatementReassignsLocal(StatementSyntax statement, string localName)
    {
        return statement.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.Left is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == localName);
    }

    private static bool IsFrameworkConfigureHttpClientMethod(IMethodSymbol method)
    {
        var containingNamespace = (method.ReducedFrom ?? method).ContainingNamespace;
        return containingNamespace.IsGlobalNamespace ||
            containingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection";
    }

    private static bool ExpressionConfiguresBaseAddress(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var body = expression switch
        {
            LambdaExpressionSyntax lambda => lambda.Body,
            AnonymousMethodExpressionSyntax anonymousMethod => anonymousMethod.Block,
            _ => null
        };

        return body is not null &&
            body.DescendantNodesAndSelf()
                .OfType<AssignmentExpressionSyntax>()
                .Any(assignment => assignment.Left is MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: "BaseAddress"
                } memberAccess &&
                    IsHttpClientBaseAddressProperty(memberAccess, semanticModel, cancellationToken));
    }

    private static bool IsHttpClientBaseAddressProperty(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(memberAccess, cancellationToken);
        if (symbolInfo.Symbol is ISymbol symbol)
        {
            return IsHttpClientBaseAddressProperty(symbol);
        }

        return symbolInfo.CandidateSymbols.Length == 0 ||
            symbolInfo.CandidateSymbols.All(IsHttpClientBaseAddressProperty);
    }

    private static bool IsHttpClientBaseAddressProperty(ISymbol symbol)
    {
        return symbol is IPropertySymbol property &&
            HttpClientSymbols.IsHttpClient(property.ContainingType);
    }

    private static bool TryGetRelativeHttpClientUrlArgument(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out ExpressionSyntax urlExpression)
    {
        urlExpression = invocation;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            !InvocationTargetsHttpClient(invocation, semanticModel, cancellationToken) ||
            !IsHttpClientReceiver(memberAccess.Expression, semanticModel, cancellationToken) ||
            invocation.ArgumentList.Arguments.Count == 0)
        {
            return false;
        }

        var candidate = invocation.ArgumentList.Arguments[0].Expression;
        if (RelativeUrlHttpMethodNames.Contains(memberAccess.Name.Identifier.ValueText, System.StringComparer.Ordinal) &&
            IsRelativeStringUrl(candidate, semanticModel, cancellationToken))
        {
            urlExpression = candidate;
            return true;
        }

        if (memberAccess.Name.Identifier.ValueText is not ("Send" or "SendAsync") ||
            !TryGetRequestMessageRelativeUrl(
                candidate,
                semanticModel,
                cancellationToken,
                out urlExpression))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetRequestMessageRelativeUrl(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out ExpressionSyntax urlExpression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        urlExpression = expression;
        if (expression is IdentifierNameSyntax identifier &&
            TryGetVisibleLocalValue(
                identifier,
                semanticModel,
                cancellationToken,
                out var localValue))
        {
            return TryGetRequestMessageRelativeUrl(
                localValue,
                semanticModel,
                cancellationToken,
                out urlExpression);
        }

        if (expression is not BaseObjectCreationExpressionSyntax requestCreation ||
            requestCreation.ArgumentList is null ||
            requestCreation.ArgumentList.Arguments.Count < 2 ||
            !IsHttpRequestMessageCreation(requestCreation, semanticModel, cancellationToken))
        {
            return false;
        }

        var candidate = requestCreation.ArgumentList.Arguments[1].Expression;
        if (!IsRelativeStringUrl(candidate, semanticModel, cancellationToken))
        {
            return false;
        }

        urlExpression = candidate;
        return true;
    }

    private static bool TryGetVisibleLocalValue(
        IdentifierNameSyntax identifier,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out ExpressionSyntax value)
    {
        value = identifier;
        if (semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol is not ILocalSymbol local ||
            identifier.FirstAncestorOrSelf<BlockSyntax>() is not { } containingBlock)
        {
            return false;
        }

        var declaration = containingBlock.Statements
            .OfType<LocalDeclarationStatementSyntax>()
            .SelectMany(statement => statement.Declaration.Variables)
            .FirstOrDefault(variable => variable.SpanStart < identifier.SpanStart &&
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetDeclaredSymbol(variable, cancellationToken),
                    local));
        if (declaration is null)
        {
            return false;
        }

        var assignment = containingBlock.Statements
            .OfType<ExpressionStatementSyntax>()
            .Select(statement => statement.Expression)
            .OfType<AssignmentExpressionSyntax>()
            .Where(candidate => candidate.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SimpleAssignmentExpression) &&
                candidate.SpanStart > declaration.Span.End &&
                candidate.SpanStart < identifier.SpanStart &&
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(candidate.Left, cancellationToken).Symbol,
                    local))
            .LastOrDefault();

        var origin = assignment?.Right ?? declaration.Initializer?.Value;
        var originEnd = assignment?.Span.End ?? declaration.Span.End;
        if (origin is null ||
            LocalIsReassignedBetween(
                local,
                originEnd,
                identifier.SpanStart,
                containingBlock,
                semanticModel,
                cancellationToken))
        {
            return false;
        }

        value = origin;
        return true;
    }

    private static bool LocalIsReassignedBetween(
        ILocalSymbol local,
        int start,
        int end,
        BlockSyntax containingBlock,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return containingBlock
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => assignment.SpanStart > start &&
                assignment.SpanStart < end &&
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol,
                    local));
    }

    private static bool IsHttpRequestMessageCreation(
        BaseObjectCreationExpressionSyntax creation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var resolvedType = semanticModel.GetTypeInfo(creation, cancellationToken).Type;
        if (resolvedType is not null && resolvedType is not IErrorTypeSymbol)
        {
            return resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                "global::System.Net.Http.HttpRequestMessage";
        }

        return creation is ObjectCreationExpressionSyntax objectCreation &&
            objectCreation.Type.ToString() is
                "HttpRequestMessage" or
                "System.Net.Http.HttpRequestMessage" or
                "global::System.Net.Http.HttpRequestMessage";
    }

    private static bool InvocationTargetsHttpClient(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return MethodTargetsHttpClient(method);
        }

        var candidateMethods = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
        return candidateMethods.Length == 0 || candidateMethods.All(MethodTargetsHttpClient);
    }

    private static bool MethodTargetsHttpClient(IMethodSymbol method)
    {
        return (method.ReducedFrom ?? method).ContainingType
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::System.Net.Http.HttpClient";
    }

    private static bool IsRelativeStringUrl(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var constant = semanticModel.GetConstantValue(expression, cancellationToken);
        if (!constant.HasValue || constant.Value is not string url || string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        return !System.Uri.TryCreate(url, System.UriKind.Absolute, out var absoluteUri) ||
            absoluteUri.Scheme is not ("http" or "https");
    }

    private static bool IsHttpClientReceiver(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var expressionType = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        if (expressionType is not null && expressionType is not IErrorTypeSymbol)
        {
            return HttpClientSymbols.IsHttpClient(expressionType);
        }

        var symbolType = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null
        };

        if (symbolType is not null && symbolType is not IErrorTypeSymbol)
        {
            return HttpClientSymbols.IsHttpClient(symbolType);
        }

        return SyntacticReceiverLooksLikeHttpClient(expression);
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

    private static string NormalizeTypeName(string typeName)
    {
        typeName = typeName.Trim();
        return typeName.StartsWith("global::", System.StringComparison.Ordinal)
            ? typeName.Substring("global::".Length)
            : typeName;
    }

#pragma warning disable RS1030 // HCR083 performs compilation-wide DI matching and needs cross-tree semantic type checks.
    private static SemanticModel GetSemanticModel(Compilation compilation, SyntaxTree syntaxTree)
    {
        return compilation.GetSemanticModel(syntaxTree);
    }
#pragma warning restore RS1030
}
