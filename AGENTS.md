# AGENTS.md

## Cursor Cloud specific instructions

This repository is a compile-time **Roslyn analyzer** library (`HttpClient.Resilience.Analyzers`).
There is no long-running service or GUI — the "application" is the analyzer, and its core
behaviour is emitting `HCRxxx` diagnostics during a normal `dotnet build`.

### Toolchain (already installed in the VM snapshot)
- .NET SDK is pinned by `global.json` (`10.0.301`, `rollForward: latestFeature`) and lives in `~/.dotnet`.
- PowerShell 7 (`pwsh`) lives in `~/.powershell` and is required only by the `scripts/Validate-*.ps1` gate scripts.
- `~/.bashrc` exports `DOTNET_ROOT`/`PATH` so `dotnet` and `pwsh` are on the PATH in interactive shells.
  Non-interactive/CI-style shells may not source `.bashrc`; if a tool is "not found", prefix commands with
  `export PATH="$HOME/.dotnet:$HOME/.powershell:$PATH"` or call the binaries by absolute path.

### Standard commands (see `CONTRIBUTING.md` for the full local gate)
- Restore: `dotnet restore HttpClient.Resilience.Analyzers.slnx`
- Build (Release): `dotnet build HttpClient.Resilience.Analyzers.slnx --configuration Release --no-restore`
- Lint / format check: `dotnet format HttpClient.Resilience.Analyzers.slnx --verify-no-changes --exclude samples`
- Test: `dotnet test HttpClient.Resilience.Analyzers.slnx --configuration Release --no-build`
- Repository/sample diagnostic gates (need `pwsh`): `./scripts/Validate-Repository.ps1` and
  `./scripts/Validate-SampleDiagnostics.ps1 -NoRestore`

### Non-obvious gotchas
- The `samples/HttpClient.Resilience.Showcase` project references the analyzer as an `Analyzer` (not a normal
  reference). Building it is expected to produce ~24 `HCR***` warnings — that is success, not failure.
  `Validate-SampleDiagnostics.ps1` builds it with `--no-incremental` and fails if any of the 19 rule IDs is missing,
  so always run it (or add `--no-incremental`) when checking diagnostics — a plain incremental rebuild can print
  `0 Warning(s)` simply because nothing recompiled.
- The showcase sample is an `Exe`; `dotnet run --project samples/HttpClient.Resilience.Showcase/...` makes a real
  outbound HTTPS request to `https://example.com` and prints the status code, so it needs network egress to succeed.
- CI runs on `windows-latest`, but everything builds/tests fine on Linux. `dotnet format` output is line-ending
  sensitive; the checked-out `.github/workflows/*.yml` files may show as modified purely due to CRLF/LF normalization
  from `.gitattributes` — this is unrelated to code changes and can be ignored.
