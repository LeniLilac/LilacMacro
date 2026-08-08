# Testing

**Status: Current.** Automated tests must be deterministic and must not control a live LilacMacro or Roblox window.

## Full validation

Run from the repository root:

```powershell
./scripts/Test-Documentation.ps1
./scripts/Test-RepositoryPolicy.ps1
dotnet restore LilacMacro.slnx --locked-mode
dotnet build LilacMacro.slnx -c Release --no-restore -warnaserror
dotnet test LilacMacro.slnx -c Release --no-build
dotnet format LilacMacro.slnx --verify-no-changes --no-restore
git diff --check
```

CI runs the documentation and repository checks before restore/build/test/format so structural failures remain quick and legible.

## Focused tests

During implementation, run the narrow test class first:

```powershell
dotnet test tests/LilacMacro.Tests/LilacMacro.Tests.csproj -c Release `
  --filter "FullyQualifiedName~OcrRuleEngineTests"
```

Replace the filter with the changed policy owner. A focused pass accelerates iteration but never replaces the full suite before handoff.

Deep-debug archive changes start with:

```powershell
dotnet test tests/LilacMacro.Tests/LilacMacro.Tests.csproj -c Release `
  --filter "FullyQualifiedName~DeepDebugSessionTests"
```

Review selector identity and Wire visual-preview changes start with:

```powershell
dotnet test tests/LilacMacro.Tests/LilacMacro.Tests.csproj -c Release `
  --filter "FullyQualifiedName~VisualPreviewTests"
```

## Test boundaries

Automated tests may use:

- pure Core models and rules;
- synthetic OCR rectangles and pixel geometry;
- temporary dataset and placement directories;
- native-input descriptors and protocol calculations that do not send live input;
- privacy-safe fake paths and manifests.

Automated tests must not require:

- a live Roblox process or game state;
- desktop focus, real cursor movement, or global hotkeys;
- Paddle installation, model downloads, GPU availability, or network access;
- owner captures, local usernames, private-server links, webhook URLs, or logs;
- computer-control tooling to operate LilacMacro or Roblox.

## Risk-based acceptance

| Change | Minimum automated evidence | Owner-only evidence |
|---|---|---|
| Documentation or policy | Documentation validator, repository policy, diff check | None unless UI guidance changed |
| Pure Core policy | Positive, negative, boundary, and malformed-input tests; full suite | None |
| Persistence/schema | Round trip, invalid data, atomic/collision behavior, full suite | Inspect migration outcome if existing owner data is involved |
| WPF layout/theme | Build, affected pure tests, full suite, format | Visual check in light and dark modes at relevant window sizes |
| Capture/window/input | Pure geometry/protocol regression tests, full suite | Owner validates Roblox sizing, focus, capture freshness, cancellation, and cleanup |
| Deep debug | Archive entries, JSONL parsing, redaction, options persistence, single-owner behavior, full suite | Owner enables it in each surface and confirms a completed operation produces a readable ZIP |
| Roblox dashboard dock | Style/exposure/activation/work-area policy tests, warning-free build, full suite | Owner validates exact `1366 x 700` client size, interactive dock, background-app focus behavior, taskbar-safe maximize, undock restoration, tab/minimize behavior, and shutdown cleanup |
| OCR worker | Serialization/timing policy tests where available, full suite | Owner validates installed CPU/GPU runtime and representative crops |
| Planned macro runtime | State-machine and fail-closed tests before live use | Owner validates bounded transitions and stop behavior in Roblox |

## Manual handoff

When a change needs live verification, provide a concise checklist with expected outcomes. Do not claim the behavior passed until the owner reports it. Capture screenshots or logs only when the owner explicitly supplies them, and keep those artifacts outside the repository.

## Failure reporting

Report the exact command, exit code, and relevant error. Separate failures introduced by the current diff from pre-existing dirty-worktree or environment failures. Never weaken a check merely to produce a green run.
