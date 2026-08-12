# Testing

**Status: Current.** Automated tests must be deterministic and must not control a live LilacMacro or Roblox window.

## Full validation

Run from the repository root:

```powershell
./scripts/Test-Documentation.ps1
./scripts/Test-Installer.ps1
./scripts/Test-RepositoryPolicy.ps1
dotnet restore LilacMacro.slnx --locked-mode
dotnet build LilacMacro.slnx -c Release --no-restore -warnaserror
dotnet test LilacMacro.slnx -c Release --no-build
dotnet format LilacMacro.slnx --verify-no-changes --no-restore
git diff --check
```

CI runs the documentation, installer, and repository checks before restore/build/test/format so structural failures remain quick and legible.

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
| WPF layout/theme | Catalog counts, unique Light/Dark gradient definitions, complete live overlay resources, persistence migration/round trip, build, full suite, format | Visual check of both palette rows, selected outline/check, live switching, readable solid/gradient surfaces, and restart persistence in light and dark modes at relevant window sizes |
| Macro display profiles | Persistence/migration and layout-policy tests, per-runner RDP profile dimensions, WPF build, full suite | Owner assigns different resolutions to two runner rows; checks `1920 x 1080` full layout with dock, closes/reopens Roblox while Dock remains enabled and confirms automatic reacquisition, checks `1366 x 768` compact layout without dock, and verifies forced minimize/restore during a compact run |
| Capture/window/input | Pure geometry/protocol regression tests, full suite | Owner validates Roblox sizing, focus, capture freshness, cancellation, and cleanup |
| Roblox settings and UI-scale normalization | Synthetic XML allowlist/preservation/malformed-input/atomic-recovery coverage; exact current-session process-name policy; panel ownership across `0.80`-`1.20`; semantic OCR without numeric text; feedback/clamp boundaries; per-session cache isolation; supplied-dataset validation; warning-free build; full suite | Owner starts with changed UI/input values on the desktop and local-runner/RDP session, confirms plan start closes Roblox, preserves unrelated XML, rejoins the private server, converges to the same rendered panel size and Lobby, then changes a previously cached environment and confirms stale-cache recalibration |
| Setup test playback | Placement plan, phantom-versus-physical unit-panel policy, and input-protocol tests; warning-free build; full suite | Owner opens the selected map at Match Prestart without camera alignment or the canonical client size, clicks Test Setup, verifies automatic `1366 x 700` sizing, alignment, and complete before/Start/after playback, confirms phantom placements accept Targeting/Auto Upgrade/Reconfigure/Sell but not Upgrade, and confirms Stop Test releases input |
| Team scrollbar A/B and fidelity test | Synthetic stable/moving/unstable thumb observations plus deterministic ramp-schedule boundaries, warning-free build, full suite | Owner opens Unit Teams, runs Drag or Scroll, confirms each reset reaches the top, and checks that the UI and `results.json` pair each requested wheel amount with its saved frames and measured normalized position |
| Team swap randomized test | Balanced randomized schedule coverage and seed determinism, translated-ROI client clipping, warning-free build, full suite | Owner opens Unit Teams, runs the chosen trial count, confirms each row reports the requested team, result, elapsed time, and terminal status, and verifies both Stop and F6 cancel without adding a failed trial |
| Deep debug | Archive entries, camel/snake-case JSONL parsing, redaction, input-marker coordinate translation, surface-specific retention, options persistence, single-owner behavior, full suite | Owner confirms Runtime Lab shows `DEEP DEBUG ON`, a run longer than 15 minutes retains its early frames, completed ZIPs open or drop into the dedicated viewer, nearby events align, click/scroll markers land correctly, and malformed entries fail visibly |
| Roblox dashboard dock | Style/exposure/activation/maintenance/work-area policy tests, including tracked post-rejoin style repair, warning-free build, full suite | Owner validates exact `1366 x 700` client size, interactive dock, post-rejoin stability without alternating dock/undock, background-app focus behavior, taskbar-safe maximize, undock restoration, tab/minimize behavior, and shutdown cleanup |
| OCR worker | Serialization/timing policy tests where available, full suite | Owner validates installed CPU/GPU runtime and representative crops |
| Planned macro runtime | State-machine and fail-closed tests before live use | Owner validates bounded transitions and stop behavior in Roblox |
| Local instance manager contracts | Multi-profile manifest/identifier bounds, legacy single-runner migration, intentional zero-runner preservation, unique endpoints/credentials/tasks, shared-vs-isolated roots, profile allowlist, native-diagnostic classification, hash-cache invalidation, rollback/state transitions, secret redaction, installer policy, warning-free build, full suite | Provision only when the owner explicitly authorizes the current task; verify removing the final runner clears its row, two visible sessions, and full UI startup without computer-control tooling |
| Local instance integration | Disposable Windows 10/11 x64 VMs: clean install, legacy migration, Runner 1/Runner 2 concurrent UI startup, shared/separate configuration, repair, update, interruption, rollback, individual/all removal, uninstall, loopback isolation, ACL/SID checks, and capture/recovery matrix | Owner-authorized device testing may exercise setup/session lifecycle without UI automation; live Roblox behavior remains owner-operated |
| Coordinated application update | Exact tag/release/four-asset/digest/checksum policy tests, trusted-redirect tests, state/request round trips, installer-script checks, warning-free build, full suite | From an older signed Program Files build, owner downloads and installs a newer public signed release, confirms every open UI closes normally, active binaries are never overwritten, owner/runner UIs relaunch at the new version, and cancellation/tampering/private-release cases fail closed |
| Runner first launch and personalization | Standard-user versus exact elevated-hive policy ownership, protected-policy negative cases, trusted-download/signature tests, managed-instance mutex identity, warning-free build, full suite | Owner repairs an existing runner and creates a new runner, confirms setup does not fail at `Software\Policies`, owner desktop is unchanged, runner desktop is black with global icons hidden, Windows privacy and Edge first-run pages stay suppressed, official Roblox login opens, trusted installer starts only when Roblox is absent, no credential is copied or entered, and Repair does not duplicate an already-open runner UI |

The native preflight must run only in its sacrificial process and cache successful evidence by exact TermService and TermWrap hashes. Disposable VMs certify the end-to-end installer/session/rollback lifecycle, not a static build catalog. Failed required patches, ARM64, active remote use during first setup, non-loopback exposure, stale/black capture, wrong pipe peers, and incomplete cleanup must remain negative tests that fail closed.

## Manual handoff

When a change needs live verification, provide a concise checklist with expected outcomes. Do not claim the behavior passed until the owner reports it. Capture screenshots or logs only when the owner explicitly supplies them, and keep those artifacts outside the repository.

## Failure reporting

Report the exact command, exit code, and relevant error. Separate failures introduced by the current diff from pre-existing dirty-worktree or environment failures. Never weaken a check merely to produce a green run.
