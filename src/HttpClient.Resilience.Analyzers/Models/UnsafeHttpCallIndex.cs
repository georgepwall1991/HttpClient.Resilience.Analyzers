using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HttpClient.Resilience.Analyzers.Models;

/// <summary>
/// Compilation-wide index of typed and named clients that visibly send unsafe HTTP methods.
/// Built on first use and shared by HCR041 and HCR042 so a solution build never pays for
/// the scan twice, and compilations with no matching handler never pay for it at all.
/// </summary>
internal sealed class UnsafeHttpCallIndex
{
    private static readonly ConditionalWeakTable<Compilation, UnsafeHttpCallIndex> Cache = new();

    private readonly Compilation _compilation;
    private readonly object _gate = new();
    private Snapshot? _snapshot;
    private int _builds;

    private UnsafeHttpCallIndex(Compilation compilation)
    {
        _compilation = compilation;
    }

    public static UnsafeHttpCallIndex GetOrCreate(Compilation compilation)
    {
        return Cache.GetValue(compilation, key => new UnsafeHttpCallIndex(key));
    }

    /// <summary>
    /// How many times the unsafe-call index for <paramref name="compilation"/> has been built.
    /// Tests assert the scan stays demand-driven and is never built more than once.
    /// </summary>
    internal static int GetBuildCount(Compilation compilation)
    {
        return Cache.TryGetValue(compilation, out var index)
            ? System.Threading.Volatile.Read(ref index._builds)
            : 0;
    }

    public Snapshot GetOrBuild(
        System.Threading.CancellationToken cancellationToken,
        System.Action? onBuilt = null)
    {
        if (System.Threading.Volatile.Read(ref _snapshot) is { } published)
        {
            return published;
        }

        lock (_gate)
        {
            if (_snapshot is { } cached)
            {
                return cached;
            }

            // A cancelled build throws before publishing, so a partial index is never cached.
            var snapshot = Build(_compilation, cancellationToken);
            System.Threading.Interlocked.Increment(ref _builds);
            onBuilt?.Invoke();
            System.Threading.Volatile.Write(ref _snapshot, snapshot);
            return snapshot;
        }
    }

    internal sealed class Snapshot
    {
        public Snapshot(
            IReadOnlyCollection<ClassDeclarationSyntax> typedClientClassesWithUnsafeCalls,
            IReadOnlyCollection<string> namedClientsWithUnsafeCalls)
        {
            TypedClientClassesWithUnsafeCalls = typedClientClassesWithUnsafeCalls;
            NamedClientsWithUnsafeCalls = namedClientsWithUnsafeCalls;
        }

        public IReadOnlyCollection<ClassDeclarationSyntax> TypedClientClassesWithUnsafeCalls { get; }

        public IReadOnlyCollection<string> NamedClientsWithUnsafeCalls { get; }

        public bool TypedClientSendsUnsafeHttpMethod(TypedClientRegistration typedClient)
        {
            return TypedClientClassesWithUnsafeCalls
                .Any(type => DeclaredTypeMatchesRegistration(type, typedClient));
        }

        public bool NamedClientSendsUnsafeHttpMethod(string clientName)
        {
            return NamedClientsWithUnsafeCalls.Contains(clientName);
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
    }

    /// <summary>
    /// Per-build state for the unsafe-call scan. Caching the constant lookup and the
    /// receiver classifications turns repeated whole-scope rescans into dictionary hits.
    /// A scan runs single-threaded under <see cref="GetOrBuild"/>'s lock.
    /// </summary>
    private sealed class UnsafeCallScan
    {
        private readonly Dictionary<(SyntaxNode Scope, string Name), bool> _localHttpClients = new();
        private readonly Dictionary<(SyntaxNode Scope, string Name), bool> _localFactories = new();
        private readonly Dictionary<(SyntaxNode Scope, string Name), bool> _memberHttpClients = new();
        private readonly Dictionary<(SyntaxNode Scope, string Name), bool> _memberFactories = new();
        private readonly IReadOnlyList<SyntaxNode> _roots;
        private readonly System.Threading.CancellationToken _cancellationToken;
        private Dictionary<(string TypeName, string ConstantName), string>? _constantStrings;

        public UnsafeCallScan(IReadOnlyList<SyntaxNode> roots, System.Threading.CancellationToken cancellationToken)
        {
            _roots = roots;
            _cancellationToken = cancellationToken;
        }

        public string? TryGetConstantString(string constantName, string? typeName)
        {
            _constantStrings ??= CollectConstantStrings(_roots, _cancellationToken);

            return _constantStrings.TryGetValue((typeName ?? string.Empty, constantName), out var value)
                ? value
                : null;
        }

        public bool GetOrAddLocalHttpClient(SyntaxNode scope, string name, System.Func<bool> compute) =>
            GetOrAdd(_localHttpClients, scope, name, compute);

        public bool GetOrAddLocalFactory(SyntaxNode scope, string name, System.Func<bool> compute) =>
            GetOrAdd(_localFactories, scope, name, compute);

        public bool GetOrAddMemberHttpClient(SyntaxNode scope, string name, System.Func<bool> compute) =>
            GetOrAdd(_memberHttpClients, scope, name, compute);

        public bool GetOrAddMemberFactory(SyntaxNode scope, string name, System.Func<bool> compute) =>
            GetOrAdd(_memberFactories, scope, name, compute);

        private static bool GetOrAdd(
            Dictionary<(SyntaxNode Scope, string Name), bool> cache,
            SyntaxNode scope,
            string name,
            System.Func<bool> compute)
        {
            var key = (scope, name);
            if (cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var computed = compute();
            cache[key] = computed;
            return computed;
        }

        private static Dictionary<(string TypeName, string ConstantName), string> CollectConstantStrings(
            IReadOnlyList<SyntaxNode> roots,
            System.Threading.CancellationToken cancellationToken)
        {
            var constants = new Dictionary<(string, string), string>();

            foreach (var root in roots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
                {
                    if (!field.Modifiers.Any(SyntaxKind.ConstKeyword) ||
                        !IsStringTypeName(field.Declaration.Type))
                    {
                        continue;
                    }

                    var declaringTypeName = (field.Parent as TypeDeclarationSyntax)?.Identifier.ValueText;
                    foreach (var variable in field.Declaration.Variables)
                    {
                        if (variable.Initializer?.Value is not { } initializer ||
                            TryGetStringLiteral(initializer) is not { } value)
                        {
                            continue;
                        }

                        var name = variable.Identifier.ValueText;
                        AddIfAbsent(constants, (string.Empty, name), value);
                        if (declaringTypeName is not null)
                        {
                            AddIfAbsent(constants, (declaringTypeName, name), value);
                        }
                    }
                }
            }

            return constants;
        }

        private static void AddIfAbsent(
            Dictionary<(string, string), string> constants,
            (string, string) key,
            string value)
        {
            if (!constants.ContainsKey(key))
            {
                constants[key] = value;
            }
        }
    }

    private static Snapshot Build(
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var roots = CompilationSyntaxIndex.GetRoots(compilation, cancellationToken);
        var scan = new UnsafeCallScan(roots, cancellationToken);

        var typedClientClassesWithUnsafeCalls = new HashSet<ClassDeclarationSyntax>();
        var namedClientsWithUnsafeCalls = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsUnsafeHttpClientCall(invocation, scan))
                {
                    AddEnclosingClasses(invocation, typedClientClassesWithUnsafeCalls);
                }

                CollectNamedClientWithUnsafeCall(invocation, scan, namedClientsWithUnsafeCalls);
            }
        }

        return new Snapshot(typedClientClassesWithUnsafeCalls, namedClientsWithUnsafeCalls);
    }

    private static void AddEnclosingClasses(SyntaxNode node, HashSet<ClassDeclarationSyntax> classes)
    {
        for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is ClassDeclarationSyntax classDeclaration)
            {
                classes.Add(classDeclaration);
            }
        }
    }

    private static void CollectNamedClientWithUnsafeCall(
        InvocationExpressionSyntax invocation,
        UnsafeCallScan scan,
        HashSet<string> namedClientsWithUnsafeCalls)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name.Identifier.ValueText != "CreateClient" ||
            invocation.ArgumentList.Arguments.Count == 0 ||
            !SyntacticReceiverLooksLikeHttpClientFactory(memberAccess.Expression, scan))
        {
            return;
        }

        var clientName = TryGetStringConstant(
            invocation.ArgumentList.Arguments[0].Expression,
            scan);
        if (clientName is null)
        {
            return;
        }

        if (IsDirectUnsafeCall(invocation, scan) ||
            AssignedClientSendsUnsafeHttpMethod(invocation, scan))
        {
            namedClientsWithUnsafeCalls.Add(clientName);
        }
    }

    private static bool IsUnsafeHttpCall(InvocationExpressionSyntax invocation, UnsafeCallScan scan)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        return IsUnsafeHttpCall(memberAccess.Name.Identifier.ValueText, invocation, scan);
    }

    private static bool IsUnsafeHttpClientCall(
        InvocationExpressionSyntax invocation,
        UnsafeCallScan scan)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            !SyntacticReceiverLooksLikeHttpClient(memberAccess.Expression, scan))
        {
            return false;
        }

        return IsUnsafeHttpCall(memberAccess.Name.Identifier.ValueText, invocation, scan);
    }

    private static bool IsUnsafeHttpCall(
        string methodName,
        InvocationExpressionSyntax invocation,
        UnsafeCallScan scan)
    {
        return HttpMethodSafety.MethodNameStartsWithUnsafePrefix(methodName) ||
            (methodName is "Send" or "SendAsync" &&
                invocation.ArgumentList.Arguments.Count > 0 &&
                RequestExpressionUsesUnsafeHttpMethod(
                    invocation.ArgumentList.Arguments[0].Expression,
                    invocation,
                    scan));
    }

    private static bool SyntacticReceiverLooksLikeHttpClient(ExpressionSyntax expression, UnsafeCallScan scan)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => ParameterLooksLikeHttpClient(identifier) ||
                LocalLooksLikeHttpClient(identifier, scan) ||
                FieldOrPropertyLooksLikeHttpClient(identifier, scan),
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax name } =>
                FieldOrPropertyLooksLikeHttpClient(name, scan),
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

    private static bool LocalLooksLikeHttpClient(IdentifierNameSyntax identifier, UnsafeCallScan scan)
    {
        if (identifier.FirstAncestorOrSelf<BlockSyntax>() is not { } block)
        {
            return false;
        }

        var name = identifier.Identifier.ValueText;
        return scan.GetOrAddLocalHttpClient(block, name, () => block
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable => variable.Identifier.ValueText == name &&
                variable.Parent is VariableDeclarationSyntax declaration &&
                IsHttpClientTypeName(declaration.Type)));
    }

    private static bool FieldOrPropertyLooksLikeHttpClient(IdentifierNameSyntax identifier, UnsafeCallScan scan)
    {
        if (identifier.FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } type)
        {
            return false;
        }

        var name = identifier.Identifier.ValueText;
        return scan.GetOrAddMemberHttpClient(type, name, () => type
            .Members
            .Any(member => member switch
            {
                FieldDeclarationSyntax field => IsHttpClientTypeName(field.Declaration.Type) &&
                    field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == name),
                PropertyDeclarationSyntax property => IsHttpClientTypeName(property.Type) &&
                    property.Identifier.ValueText == name,
                _ => false
            }));
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
        UnsafeCallScan scan)
    {
        expression = SyntaxTransparency.Unwrap(expression);

        return expression switch
        {
            ObjectCreationExpressionSyntax objectCreation => HttpRequestCreationUsesUnsafeMethod(objectCreation, scan),
            ImplicitObjectCreationExpressionSyntax implicitObjectCreation =>
                HttpRequestCreationUsesUnsafeMethod(implicitObjectCreation, scan),
            IdentifierNameSyntax identifier => LocalRequestVariableUsesUnsafeMethod(identifier, context, scan),
            _ => false
        };
    }

    private static bool HttpRequestCreationUsesUnsafeMethod(
        BaseObjectCreationExpressionSyntax objectCreation,
        UnsafeCallScan scan)
    {
        return objectCreation.ArgumentList?.Arguments
            .Select(argument => SyntaxTransparency.Unwrap(argument.Expression))
            .Any(expression => IsUnsafeHttpMethodExpression(expression, scan)) == true ||
            objectCreation.Initializer?.Expressions
                .OfType<AssignmentExpressionSyntax>()
                .Any(assignment => IsMethodMember(assignment.Left) &&
                    IsUnsafeHttpMethodExpression(SyntaxTransparency.Unwrap(assignment.Right), scan)) == true;
    }

    private static bool LocalRequestVariableUsesUnsafeMethod(
        IdentifierNameSyntax identifier,
        SyntaxNode context,
        UnsafeCallScan scan)
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
                !SyntaxTransparency.LocalIsReassignedBetween(
                    containingBlock,
                    identifier.Identifier.ValueText,
                    variable.SpanStart,
                    context.SpanStart) &&
                RequestExpressionUsesUnsafeHttpMethod(variable.Initializer!.Value, variable, scan));
    }

    private static bool IsMethodMember(ExpressionSyntax expression)
    {
        expression = SyntaxTransparency.Unwrap(expression);

        return expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "Method",
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText == "Method",
            _ => false
        };
    }

    private static bool IsUnsafeHttpMethodExpression(ExpressionSyntax expression, UnsafeCallScan scan)
    {
        expression = SyntaxTransparency.Unwrap(expression);

        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess when
                memberAccess.Expression.ToString() == "HttpMethod" =>
                HttpMethodSafety.IsUnsafeHttpMethodName(memberAccess.Name.Identifier.ValueText, ignoreCase: false),
            ObjectCreationExpressionSyntax objectCreation when objectCreation.Type.ToString() == "HttpMethod" =>
                objectCreation.ArgumentList?.Arguments.Count > 0 &&
                TryGetStringConstant(objectCreation.ArgumentList.Arguments[0].Expression, scan) is { } method &&
                HttpMethodSafety.IsUnsafeHttpMethodName(method, ignoreCase: true),
            _ => false
        };
    }

    private static bool SyntacticReceiverLooksLikeHttpClientFactory(ExpressionSyntax expression, UnsafeCallScan scan)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => ParameterLooksLikeHttpClientFactory(identifier) ||
                LocalLooksLikeHttpClientFactory(identifier, scan) ||
                FieldOrPropertyLooksLikeHttpClientFactory(identifier, scan),
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax name } =>
                FieldOrPropertyLooksLikeHttpClientFactory(name, scan),
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

    private static bool LocalLooksLikeHttpClientFactory(IdentifierNameSyntax identifier, UnsafeCallScan scan)
    {
        if (identifier.FirstAncestorOrSelf<BlockSyntax>() is not { } block)
        {
            return false;
        }

        var name = identifier.Identifier.ValueText;
        return scan.GetOrAddLocalFactory(block, name, () => block
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Any(variable => variable.Identifier.ValueText == name &&
                variable.Parent is VariableDeclarationSyntax declaration &&
                IsHttpClientFactoryTypeName(declaration.Type)));
    }

    private static bool FieldOrPropertyLooksLikeHttpClientFactory(IdentifierNameSyntax identifier, UnsafeCallScan scan)
    {
        if (identifier.FirstAncestorOrSelf<TypeDeclarationSyntax>() is not { } type)
        {
            return false;
        }

        var name = identifier.Identifier.ValueText;
        return scan.GetOrAddMemberFactory(type, name, () => type
            .Members
            .Any(member => member switch
            {
                FieldDeclarationSyntax field => IsHttpClientFactoryTypeName(field.Declaration.Type) &&
                    field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == name),
                PropertyDeclarationSyntax property => IsHttpClientFactoryTypeName(property.Type) &&
                    property.Identifier.ValueText == name,
                _ => false
            }));
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
        UnsafeCallScan scan)
    {
        return createClientInvocation.Parent is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Parent is InvocationExpressionSyntax invocation &&
            IsUnsafeHttpCall(invocation, scan);
    }

    private static bool AssignedClientSendsUnsafeHttpMethod(
        InvocationExpressionSyntax createClientInvocation,
        UnsafeCallScan scan)
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
                !SyntaxTransparency.LocalIsReassignedBetween(
                    containingBlock,
                    localName,
                    declarator.SpanStart,
                    invocation.SpanStart) &&
                IsUnsafeHttpCall(invocation, scan));
    }

    private static string? TryGetStringLiteral(ExpressionSyntax expression)
    {
        return expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;
    }

    private static string? TryGetStringConstant(ExpressionSyntax expression, UnsafeCallScan scan)
    {
        expression = SyntaxTransparency.Unwrap(expression);

        if (TryGetStringLiteral(expression) is { } literal)
        {
            return literal;
        }

        return expression switch
        {
            IdentifierNameSyntax identifier => TryGetLocalStringConstant(identifier) ??
                scan.TryGetConstantString(identifier.Identifier.ValueText, typeName: null),
            MemberAccessExpressionSyntax memberAccess => scan.TryGetConstantString(
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
}
