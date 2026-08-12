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

    private sealed class UnsafeCallIndex
    {
        public UnsafeCallIndex(
            IReadOnlyCollection<ClassDeclarationSyntax> typedClientClassesWithUnsafeCalls,
            IReadOnlyCollection<string> namedClientsWithUnsafeCalls)
        {
            TypedClientClassesWithUnsafeCalls = typedClientClassesWithUnsafeCalls;
            NamedClientsWithUnsafeCalls = namedClientsWithUnsafeCalls;
        }

        public IReadOnlyCollection<ClassDeclarationSyntax> TypedClientClassesWithUnsafeCalls { get; }

        public IReadOnlyCollection<string> NamedClientsWithUnsafeCalls { get; }
    }

    /// <summary>
    /// Builds the unsafe-call index on first use instead of at compilation start. Most
    /// compilations never call <c>AddStandardResilienceHandler</c>, and those must not pay
    /// for a scan of the whole syntax forest.
    /// </summary>
    private sealed class DeferredUnsafeCallIndex
    {
        private readonly Compilation _compilation;
        private readonly System.Action _onBuilt;
        private readonly object _gate = new();
        private UnsafeCallIndex? _index;

        public DeferredUnsafeCallIndex(Compilation compilation, System.Action onBuilt)
        {
            _compilation = compilation;
            _onBuilt = onBuilt;
        }

        public UnsafeCallIndex GetOrBuild(System.Threading.CancellationToken cancellationToken)
        {
            if (System.Threading.Volatile.Read(ref _index) is { } published)
            {
                return published;
            }

            lock (_gate)
            {
                if (_index is { } cached)
                {
                    return cached;
                }

                // A cancelled build throws before publishing, so a partial index is never cached.
                var index = BuildUnsafeCallIndex(_compilation, cancellationToken);
                _onBuilt();
                System.Threading.Volatile.Write(ref _index, index);
                return index;
            }
        }
    }

    /// <summary>
    /// Per-build state for the unsafe-call scan. Caching the constant lookup and the
    /// receiver classifications turns repeated whole-scope rescans into dictionary hits.
    /// A scan runs single-threaded under <see cref="DeferredUnsafeCallIndex"/>'s lock.
    /// </summary>
    private sealed class UnsafeCallScan
    {
        private readonly Dictionary<(SyntaxNode Scope, string Name), bool> _localHttpClients = new();
        private readonly Dictionary<(SyntaxNode Scope, string Name), bool> _localFactories = new();
        private readonly Dictionary<(SyntaxNode Scope, string Name), bool> _memberHttpClients = new();
        private readonly Dictionary<(SyntaxNode Scope, string Name), bool> _memberFactories = new();
        private readonly Dictionary<(string TypeName, string ConstantName), string> _constantStrings;

        public UnsafeCallScan(IReadOnlyList<SyntaxNode> roots, System.Threading.CancellationToken cancellationToken)
        {
            Roots = roots;
            _constantStrings = CollectConstantStrings(roots, cancellationToken);
        }

        public IReadOnlyList<SyntaxNode> Roots { get; }

        public string? TryGetConstantString(string constantName, string? typeName)
        {
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

        /// <summary>
        /// Indexes every constant string field once. Keys are stored both unqualified and
        /// qualified by declaring type name, and the first literal-initialized declaration
        /// wins, which matches the document-order lookup this replaces.
        /// </summary>
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

    private int _unsafeCallIndexBuilds;

    /// <summary>
    /// How many times this analyzer instance has built the unsafe-call index. Tests assert
    /// the index stays demand-driven and is never built more than once per compilation.
    /// </summary>
    internal int UnsafeCallIndexBuilds => System.Threading.Volatile.Read(ref _unsafeCallIndexBuilds);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(AnalyzeCompilation);
    }

    private void AnalyzeCompilation(CompilationStartAnalysisContext context)
    {
        var unsafeCallIndex = new DeferredUnsafeCallIndex(
            context.Compilation,
            () => System.Threading.Interlocked.Increment(ref _unsafeCallIndexBuilds));

        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeInvocation(nodeContext, unsafeCallIndex),
            SyntaxKind.InvocationExpression);
    }

    private static UnsafeCallIndex BuildUnsafeCallIndex(
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var roots = CompilationSyntaxIndex.GetRoots(compilation, cancellationToken);
        var scan = new UnsafeCallScan(roots, cancellationToken);

        // One invocation pass per tree. An unsafe call is owned by every enclosing class
        // declaration, which is what scanning each class's descendants used to establish.
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

        return new UnsafeCallIndex(typedClientClassesWithUnsafeCalls, namedClientsWithUnsafeCalls);
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

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        DeferredUnsafeCallIndex unsafeCallIndex)
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

        if (typedClient is not null &&
            TypedClientSendsUnsafeHttpMethod(unsafeCallIndex.GetOrBuild(context.CancellationToken), typedClient))
        {
            ReportDiagnostic(context, invocation);
            return;
        }

        var namedClient = FindNamedClientInChain(invocation, context.SemanticModel, context.CancellationToken);
        namedClient ??= FindNamedClientForBuilderLocal(invocation, context.SemanticModel, context.CancellationToken);
        if (namedClient is not null &&
            NamedClientSendsUnsafeHttpMethod(unsafeCallIndex.GetOrBuild(context.CancellationToken), namedClient))
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
            ContainsSafeOnlyRetryPredicate(invocation, semanticModel, cancellationToken);
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

    private static bool ContainsSafeOnlyRetryPredicate(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return invocation
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment => IsSafeOnlyShouldHandleAssignment(
                assignment,
                semanticModel,
                cancellationToken));
    }

    private static bool IsSafeOnlyShouldHandleAssignment(
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (assignment.Left is not MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "ShouldHandle"
            } shouldHandleMember ||
            !IsFrameworkShouldHandleProperty(shouldHandleMember, semanticModel, cancellationToken))
        {
            return false;
        }

        var predicateExpression = GetPredicateExpression(assignment.Right);

        return predicateExpression is not null &&
            IsSafeOnlyPredicateExpression(predicateExpression, semanticModel, cancellationToken);
    }

    private static ExpressionSyntax? GetPredicateExpression(ExpressionSyntax expression)
    {
        if (expression is not LambdaExpressionSyntax lambda)
        {
            return null;
        }

        if (lambda.Body is ExpressionSyntax expressionBody)
        {
            return expressionBody;
        }

        return lambda.Body is BlockSyntax { Statements.Count: 1 } block &&
            block.Statements[0] is ReturnStatementSyntax { Expression: { } returnExpression }
                ? returnExpression
                : null;
    }

    private static bool IsSafeOnlyPredicateExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        return expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => IsSafeOnlyPredicateExpression(
                parenthesized.Expression,
                semanticModel,
                cancellationToken),
            PostfixUnaryExpressionSyntax postfix when
                postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) =>
                IsSafeOnlyPredicateExpression(postfix.Operand, semanticModel, cancellationToken),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalOrExpression) =>
                IsSafeOnlyPredicateExpression(binary.Left, semanticModel, cancellationToken) &&
                IsSafeOnlyPredicateExpression(binary.Right, semanticModel, cancellationToken),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression) =>
                IsSafeOnlyPredicateExpression(binary.Left, semanticModel, cancellationToken) ||
                IsSafeOnlyPredicateExpression(binary.Right, semanticModel, cancellationToken),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) =>
                IsSafeHttpMethodEquality(binary, semanticModel, cancellationToken),
            InvocationExpressionSyntax invocation =>
                IsSafeHttpMethodEqualsInvocation(invocation, semanticModel, cancellationToken),
            _ => false
        };
    }

    private static bool IsSafeHttpMethodEqualsInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (invocation.ArgumentList.Arguments.Count == 1 &&
            invocation.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Equals",
                Expression: MemberAccessExpressionSyntax httpMethodMember
            } &&
            IsFrameworkHttpMethodMember(httpMethodMember, semanticModel, cancellationToken) &&
            SafeHttpMethodNames.Contains(
                httpMethodMember.Name.Identifier.ValueText,
                System.StringComparer.Ordinal))
        {
            return true;
        }

        if (invocation.ArgumentList.Arguments.Count != 2 ||
            !IsSystemObjectEqualsInvocation(invocation, semanticModel, cancellationToken))
        {
            return false;
        }

        var httpMethodMembers = invocation.ArgumentList.Arguments
            .SelectMany(argument => argument.Expression
                .DescendantNodesAndSelf()
                .OfType<MemberAccessExpressionSyntax>())
            .Where(memberAccess => IsFrameworkHttpMethodMember(memberAccess, semanticModel, cancellationToken))
            .Select(memberAccess => memberAccess.Name.Identifier.ValueText)
            .ToArray();

        return httpMethodMembers.Any(method => SafeHttpMethodNames.Contains(method, System.StringComparer.Ordinal)) &&
            !httpMethodMembers.Any(method => UnsafeHttpMethodNames.Contains(method, System.StringComparer.Ordinal));
    }

    private static bool IsSystemObjectEqualsInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method)
        {
            return IsSystemObjectEqualsMethod(method);
        }

        var candidateMethods = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToArray();
        return candidateMethods.Length > 0 && candidateMethods.All(IsSystemObjectEqualsMethod);
    }

    private static bool IsSystemObjectEqualsMethod(IMethodSymbol method)
    {
        return method.Name == "Equals" &&
            (method.ReducedFrom ?? method).ContainingType.SpecialType == SpecialType.System_Object;
    }

    private static bool IsSafeHttpMethodEquality(
        BinaryExpressionSyntax binary,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var httpMethodMembers = binary
            .ChildNodes()
            .OfType<ExpressionSyntax>()
            .Select(UnwrapTransparentExpressions)
            .SelectMany(operand => operand.DescendantNodesAndSelf())
            .OfType<MemberAccessExpressionSyntax>()
            .Where(memberAccess => IsFrameworkHttpMethodMember(memberAccess, semanticModel, cancellationToken))
            .Select(memberAccess => memberAccess.Name.Identifier.ValueText)
            .ToArray();

        return httpMethodMembers.Any(method => SafeHttpMethodNames.Contains(method, System.StringComparer.Ordinal)) &&
            !httpMethodMembers.Any(method => UnsafeHttpMethodNames.Contains(method, System.StringComparer.Ordinal));
    }

    private static bool IsFrameworkShouldHandleProperty(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(memberAccess, cancellationToken);
        if (symbolInfo.Symbol is ISymbol symbol)
        {
            return IsFrameworkShouldHandleProperty(symbol);
        }

        return symbolInfo.CandidateSymbols.Length == 0 ||
            symbolInfo.CandidateSymbols.All(IsFrameworkShouldHandleProperty);
    }

    private static bool IsFrameworkShouldHandleProperty(ISymbol symbol)
    {
        return symbol is IPropertySymbol property &&
            (property.ContainingNamespace.IsGlobalNamespace ||
                property.ContainingNamespace.ToDisplayString() == "Polly.Retry");
    }

    private static bool IsFrameworkHttpMethodMember(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(memberAccess, cancellationToken);
        if (symbolInfo.Symbol is ISymbol symbol)
        {
            return IsFrameworkHttpMethodMember(symbol);
        }

        return symbolInfo.CandidateSymbols.Length == 0 ||
            symbolInfo.CandidateSymbols.All(IsFrameworkHttpMethodMember);
    }

    private static bool IsFrameworkHttpMethodMember(ISymbol symbol)
    {
        return symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::System.Net.Http.HttpMethod";
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
            .Select(variable => UnwrapTransparentExpressions(variable.Initializer!.Value))
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

    private static bool TypedClientSendsUnsafeHttpMethod(
        UnsafeCallIndex unsafeCallIndex,
        TypedClientRegistration typedClient)
    {
        return unsafeCallIndex.TypedClientClassesWithUnsafeCalls
            .Any(type => DeclaredTypeMatchesRegistration(type, typedClient));
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
        UnsafeCallIndex unsafeCallIndex,
        string clientName)
    {
        return unsafeCallIndex.NamedClientsWithUnsafeCalls.Contains(clientName);
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
        return UnsafeHttpMethodPrefixes.Any(prefix => methodName.StartsWith(prefix, System.StringComparison.Ordinal)) ||
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
        expression = UnwrapTransparentExpressions(expression);

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
            .Select(argument => UnwrapTransparentExpressions(argument.Expression))
            .Any(expression => IsUnsafeHttpMethodExpression(expression, scan)) == true ||
            objectCreation.Initializer?.Expressions
                .OfType<AssignmentExpressionSyntax>()
                .Any(assignment => IsMethodMember(assignment.Left) &&
                    IsUnsafeHttpMethodExpression(UnwrapTransparentExpressions(assignment.Right), scan)) == true;
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
                !LocalIsReassignedBetween(
                    containingBlock,
                    identifier.Identifier.ValueText,
                    variable.SpanStart,
                    context.SpanStart) &&
                RequestExpressionUsesUnsafeHttpMethod(variable.Initializer!.Value, variable, scan));
    }

    private static bool IsMethodMember(ExpressionSyntax expression)
    {
        expression = UnwrapTransparentExpressions(expression);

        return expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == "Method",
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText == "Method",
            _ => false
        };
    }

    private static bool IsUnsafeHttpMethodExpression(ExpressionSyntax expression, UnsafeCallScan scan)
    {
        expression = UnwrapTransparentExpressions(expression);

        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess when
                memberAccess.Expression.ToString() == "HttpMethod" =>
                UnsafeHttpMethodNames.Contains(memberAccess.Name.Identifier.ValueText, System.StringComparer.Ordinal),
            ObjectCreationExpressionSyntax objectCreation when objectCreation.Type.ToString() == "HttpMethod" =>
                objectCreation.ArgumentList?.Arguments.Count > 0 &&
                TryGetStringConstant(objectCreation.ArgumentList.Arguments[0].Expression, scan) is { } method &&
                UnsafeHttpMethodNames.Contains(method, System.StringComparer.OrdinalIgnoreCase),
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
                !LocalIsReassignedBetween(containingBlock, localName, declarator.SpanStart, invocation.SpanStart) &&
                IsUnsafeHttpCall(invocation, scan));
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

    private static string? TryGetStringConstant(ExpressionSyntax expression, UnsafeCallScan scan)
    {
        expression = UnwrapTransparentExpressions(expression);

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
