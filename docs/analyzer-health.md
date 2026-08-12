# Analyzer Health

Reviewed: 2026-08-03
Repository: `HttpClient.Resilience.Analyzers`
Baseline: `main` at `v0.1.142`

This document is the repository-specific health audit for the shipped HCR analyzers. It replaces the stale AutoMapper-oriented audit that was previously available only through the generic analyzer-health skill. It is planning and review context, not a substitute for reading the current analyzer, code fix, test, sample, and release code.

## Evidence and quality bar

The baseline was inspected from the 19 analyzer implementations, 10 code-fix providers, rule documentation, the showcase sample, the 31 test source files, `scripts/Validate-Repository.ps1`, `scripts/Validate-SampleDiagnostics.ps1`, the package-consumption validator, the release workflow, and the current `main` history. Every planned change must preserve the following contract:

- A diagnostic is emitted only when Roslyn symbols and visible syntax provide enough evidence to name the production risk.
- Lookalike APIs, unresolved custom wrappers, reassigned values, tests, and intentional configuration stay quiet unless the rule explicitly documents otherwise.
- Automatic fixes must be executable and preserve surrounding policy. Advisory or manual-review actions must say so in their title and documentation.
- Every rule keeps synchronized diagnostic metadata, release notes, README/catalog entries, rule docs, profiles, samples, and focused positive/negative tests.
- Verification covers format, repository invariants, sample diagnostics, build, tests, package validation, and package consumption before a release tag.

## Current scorecard

Scores use a 1–5 scale. `Analyzer` measures semantic depth and diagnostic placement; `False positives` measures conservative ownership and lookalike filtering; `Fix strategy` measures executable safety and Fix All behavior; `Tests` measures positive, negative, edge, and code-fix coverage; `Docs/samples` measures rule and adoption guidance; `Importance` measures user impact. Priority is the next useful health investment, not a claim that existing rules are unsafe.

| Rule | Domain | Severity | Analyzer | False positives | Fix strategy | Tests | Docs/samples | Importance | Priority | Current gap |
|---|---|---:|---:|---:|---:|---:|---:|---:|---|---|
| HCR001 | Lifetime | Warning | 4 | 4 | 2 | 4 | 4 | 5 | P1 | Factory code fix is intentionally partial; safe request-path shapes need broader executable coverage. |
| HCR002 | Lifetime | Warning | 4 | 4 | 3 | 5 | 4 | 5 | P2 | Assignment/property diagnostics remain guidance-only; parameterless field rewrite needs more safety cases. |
| HCR003 | Lifetime | Warning | 4 | 4 | 1 | 5 | 4 | 5 | P2 | No automatic fix because ownership and lifetime policy require manual review. |
| HCR004 | Typed clients | Warning | 4 | 4 | 1 | 5 | 4 | 5 | P2 | Registration graph is strong; the diagnostic is guidance-only and needs clearer repair patterns. |
| HCR005 | Typed clients | Warning | 4 | 4 | 4 | 5 | 5 | 4 | P2 | Duplicate-removal fix must keep policy-bearing registration chains on manual review. |
| HCR020 | Handlers | Warning | 4 | 4 | 1 | 5 | 4 | 5 | P2 | Scoped dependency ownership is reported but intentionally has no automatic rewrite. |
| HCR040 | Resilience | Warning | 4 | 4 | 4 | 5 | 4 | 4 | P2 | Chained and reassigned builder shapes need continued fix-safety coverage. |
| HCR041 | Resilience | Warning | 4 | 4 | 4 | 5 | 5 | 5 | P2 | Retry-policy fixes need more preservation tests for custom predicates and configuration. |
| HCR060 | Response lifetime | Warning | 4 | 4 | 4 | 5 | 4 | 5 | P1 | Disposal fix is limited to simple declarations and adjacent assignments. |
| HCR061 | Response lifetime | Warning | 4 | 4 | 3 | 5 | 4 | 5 | P1 | Success-check insertion is safe for known shapes but still partial for complex ownership/control flow. |
| HCR062 | Response lifetime | Warning | 4 | 5 | 1 | 4 | 4 | 4 | P2 | Shared-header ownership is high-confidence; guidance should better show request-message migration. |
| HCR063 | Response lifetime | Warning | 4 | 4 | 3 | 5 | 4 | 5 | P1 | Await fix is limited to async containing functions and simple blocking forms. |
| HCR064 | Response lifetime | Warning | 4 | 4 | 4 | 5 | 4 | 5 | P1 | Token selection and overload preservation need more edge-case and Fix All tests. |
| HCR080 | Concurrency | Info | 4 | 4 | 1 | 5 | 4 | 4 | P2 | Heuristic suggestion has no automatic fix; bounded alternatives need clearer samples. |
| HCR081 | Response lifetime | Warning | 4 | 4 | 3 | 5 | 4 | 5 | P1 | Stream disposal fix is limited for nested/non-adjacent ownership transfers. |
| HCR082 | Resilience | Warning | 4 | 4 | 1 | 4 | 4 | 4 | P2 | Request-path pipeline ownership is intentionally guidance-only. |
| HCR083 | Typed clients | Warning | 4 | 4 | 1 | 5 | 4 | 5 | P1 | Relative-URL findings need a safe BaseAddress repair path where configuration is unambiguous. |
| HCR084 | Named clients | Warning | 4 | 4 | 1 | 5 | 4 | 4 | P2 | Constant-backed and cross-file naming guidance should remain explicit and conservative. |
| HCR085 | Typed clients | Warning | 4 | 4 | 1 | 5 | 4 | 5 | P1 | Implicit-name collision report is strong; the repair guidance should cover explicit-name migration. |

## Prioritized 30-iteration backlog

Each iteration is independently branched from a clean synchronized `main`, reviewed in a pull request, merged to `main`, and tagged with the next stable patch version. The order is deliberately narrow so one regression can be isolated and reverted without bundling unrelated rule behavior.

| Iteration | Area | Deliverable | Exit evidence |
|---:|---|---|---|
| 1 | Health contract | Add this audit, link it from docs, and make repository validation require it. | Health document present; validator passes. |
| 2 | HCR001 | Add a safe code-fix case for an existing factory parameter in a local function. | Positive, negative, and fixed-source tests. |
| 3 | HCR001 | Cover primary-constructor factory ownership in the code-fix documentation and sample. | Focused tests plus synchronized docs/sample. |
| 4 | HCR002 | Add negative tests for configured handlers and reassignment before a client initializer. | Targeted analyzer suite and docs remain green. |
| 5 | HCR002 | Harden the parameterless field code fix against existing initializer trivia and modifiers. | Fixed-source and formatting tests. |
| 6 | HCR005 | Add policy-chain coverage for duplicate registration Fix All behavior. | Positive, negative, and Fix All tests. |
| 7 | HCR020 | Expand scoped dependency wrapper coverage and document the manual-review boundary. | Analyzer tests and rule guidance. |
| 8 | HCR040 | Preserve builder chains and comments when removing duplicate standard handlers. | Code-fix tests and format verification. |
| 9 | HCR041 | Add retry-predicate preservation cases for custom `HttpMethod` expressions. | Positive/negative/code-fix tests. |
| 10 | HCR060 | Add safe disposal fixes for a wider set of adjacent declaration/assignment trivia shapes. | Fixed-source tests and no-fix guards. |
| 11 | HCR061 | Add success-check placement coverage for nested conditional content reads. | Analyzer and fixed-source tests. |
| 12 | HCR063 | Extend async code-fix coverage for `ConfigureAwait` task wrappers. | Positive, negative, and fixed-source tests. |
| 13 | HCR064 | Verify token overload selection when both a token and a token source are visible. | Focused analyzer/code-fix tests. |
| 14 | HCR081 | Add `await using` guidance and explicit no-fix coverage for async-only ownership. | Rule docs, tests, and sample. |
| 15 | HCR083 | Document and test safe BaseAddress configuration through typed-client builder locals. | Analyzer tests and docs. |
| 16 | HCR084 | Add conservative cross-file constant-backed named-client guidance. | Positive/negative tests; no speculative fix. |
| 17 | HCR085 | Add explicit-name migration examples and diagnostic message coverage. | Rule docs, sample, and tests. |
| 18 | Diagnostic UX | Tighten one high-volume diagnostic location to the offending member token. | Location assertions and snapshot update. |
| 19 | Catalog trust | Add a machine-readable rule metadata check for severity/category/docs parity. | Validator test and generated report. |
| 20 | Samples | Make sample diagnostics exercise every shipped rule at least once. | Sample validation and documented snapshot. |
| 21 | Package validation | Add a packed-analyzer smoke test for representative warnings and code fixes. | Package-consumption validator passes. |
| 22 | Fix All | Add a shared regression suite for equivalence keys and repeated diagnostics. | Fix All tests across two representative rules. |
| 23 | False positives | Add lookalike API fixtures for the typed-client and response-lifetime families. | Negative analyzer tests. |
| 24 | Performance | Add compilation-size guardrails for compilation-wide registration analyzers. | Done. `AnalyzerWorkGuardrailTests` bounds registration scans, receiver classifications, and HCR041 index builds. |
| 25 | Documentation | Generate a rule inventory table from diagnostic metadata and verify drift. | Generated docs check passes. |
| 26 | Release tooling | Make release-version validation report package/tag/README mismatches together. | Script tests and clear failure output. |
| 27 | Adoption | Add a brownfield example showing targeted suppression with ownership rationale. | Docs/sample validation. |
| 28 | CI | Add a focused workflow job for rule-catalog and sample-diagnostic drift. | Workflow syntax and local script pass. |
| 29 | Audit refresh | Re-score all rules from the accumulated test and release evidence. | Updated scorecard and backlog notes. |
| 30 | Stable release | Run the complete verification stack, publish the final health audit, and tag the thirtieth release. | Clean `main`, 30 new tags, and release metadata aligned. |

## Analyzer throughput

Compilation-wide work is bounded by three deterministic invariants, each asserted by
`tests/HttpClient.Resilience.Analyzers.Tests/AnalyzerWorkGuardrailTests.cs`:

- A registration scan runs once per compilation, however many analyzers ask for it, and its
  cost grows linearly with compilation size.
- Only invocations whose method name matches a registration API (`AddHttpClient`,
  `AddSingleton`, `AddScoped`, `AddTransient`) have their receiver classified. Classification
  needs a semantic binding and, on failure, a scope-wide syntactic search, so it must never
  run for arbitrary member invocations.
- HCR041 builds its unsafe-call index only when a compilation actually registers a standard
  resilience handler, and never more than once.

Behavior is pinned independently by `AnalyzerBehaviorSnapshotTests`, which compares every
analyzer's output over a fixed corpus against `tests/HttpClient.Resilience.Analyzers.Tests/Corpus/expected-diagnostics.txt`.
Performance work is expected to leave that baseline byte-identical. `AnalyzerRobustnessTests`
covers the other failure mode: an analyzer that throws is disabled by Roslyn behind an
`AD0001`, so every rule is run over parse errors, unresolved symbols, and unusual declaration
forms and required not to throw.

## Verification baseline

For every iteration, run the narrowest affected test project first, then the repository gates before merging:

```powershell
dotnet format HttpClient.Resilience.Analyzers.slnx --verify-no-changes --exclude samples
./scripts/Validate-Repository.ps1
./scripts/Validate-SampleDiagnostics.ps1 -NoRestore
dotnet build HttpClient.Resilience.Analyzers.slnx --configuration Release --no-restore
dotnet test HttpClient.Resilience.Analyzers.slnx --configuration Release --no-build
```

When packaging files or analyzer delivery changes, also run `Validate-Package.ps1` and `Validate-PackageConsumption.ps1` against the freshly packed `.nupkg`. A release tag is valid only when it matches the package's stable three-part version and points at the merged `main` commit.

## Known limitations

- The analyzers intentionally use visible, symbol-resolved evidence rather than attempting arbitrary interprocedural or configuration-driven inference.
- HCR080 is suggestion-level and heuristic; it should not claim that every fan-out requires a specific concurrency limit.
- HCR001, HCR060, HCR061, HCR063, HCR081, and HCR083 expose partial or guidance-only code-fix coverage where an automatic rewrite could otherwise discard ownership or policy.
- Documentation and generated artifacts must be treated as part of diagnostic trust, not as release decoration.
