## Summary

-

## Verification

- [ ] `dotnet format HttpClient.Resilience.Analyzers.slnx --verify-no-changes --exclude samples`
- [ ] `./scripts/Validate-Repository.ps1`
- [ ] `dotnet build HttpClient.Resilience.Analyzers.slnx --configuration Release --no-restore`
- [ ] `./scripts/Validate-SampleDiagnostics.ps1 -NoRestore`
- [ ] `dotnet test HttpClient.Resilience.Analyzers.slnx --configuration Release --no-build`
- [ ] Package validation, if packaging metadata or analyzer delivery changed

## Notes

-
