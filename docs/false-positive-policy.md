# False Positive Policy

Rules should prefer fewer, stronger diagnostics. Promote a heuristic to warning only when the analyzer has enough evidence to make the diagnostic actionable.

## Rule Bar

- A warning should identify a concrete outbound HTTP risk, not a style preference.
- Analyzer logic should skip cases where ownership, lifetimes, or retry behavior are unknown rather than guessing.
- Heuristic rules should start below warning severity until they prove stable in real code.
- Code fixes should be local and boring. They should not silently change service lifetimes, retry semantics, or API contracts.

## Reporting Issues

Useful false-positive reports include:

- The diagnostic ID.
- A minimal code sample.
- The expected behavior and why the current code is safe.
- Whether the code uses generated sources, custom DI wrappers, custom resilience extensions, or ownership-transfer abstractions.

## Suppressions

Suppressions should be rare and justified near the code. Prefer a short production reason over a generic comment:

```csharp
#pragma warning disable HCR060 // Response ownership is transferred to StreamingResult, which disposes it.
return new StreamingResult(response);
#pragma warning restore HCR060
```

If a pattern needs repeated suppression across a codebase, that is evidence for improving the analyzer or documenting a safe project-specific wrapper.
