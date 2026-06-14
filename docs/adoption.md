# Adoption

Start with `profiles/brownfield-adoption.editorconfig` when introducing the analyzer to an existing codebase. Move to `profiles/default.editorconfig` once the high-confidence warnings are understood.

## Suggested Rollout

1. Add the analyzer package without changing build gates.
2. Copy `profiles/brownfield-adoption.editorconfig` into the repository or import its severities into the existing `.editorconfig`.
3. Review `HCR040`, `HCR041`, and `HCR060` first. These are intentionally kept at warning level in the brownfield profile because they point at retry and response-lifetime risks that can create incidents.
4. Fix or explicitly suppress existing findings with a reason.
5. Move to `profiles/default.editorconfig` once new warnings are actionable for the team.
6. Use `profiles/strict-ci.editorconfig` only after the current warning baseline is clean.

## Baseline Strategy

Prefer fixing high-confidence findings over bulk suppressing them. When a warning is intentionally accepted, keep the suppression close to the code and explain the production reason:

```csharp
#pragma warning disable HCR041 // Endpoint uses idempotency keys and is safe to retry.
services.AddHttpClient<PaymentsClient>()
    .AddStandardResilienceHandler();
#pragma warning restore HCR041
```

For large services, start with `HCR041` and `HCR060` in the most critical outbound paths, then work through lifetime and DI findings.
