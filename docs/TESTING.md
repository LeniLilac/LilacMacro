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

Runtime search-region or bundled-evidence changes start with:

```powershell
./scripts/Test-RuntimeEvidence.ps1
dotnet test tests/LilacMacro.Tests/LilacMacro.Tests.csproj -c Release `
  --filter "FullyQualifiedName~RuntimeEvidencePolicyTests"
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
| Runtime search region or evidence bundle | Exact annotation-label uniqueness, catalog-to-manifest geometry, static-region inventory, bundle inventory/hash validation, missing-label and duplicate-label negatives, repository policy, warning-free build, full suite | Owner verifies the state at each represented UI scale and confirms the search area neither misses its owner nor absorbs an overlapping layer |
| Pure Core policy | Positive, negative, boundary, and malformed-input tests; full suite | None |
| Temporal runtime transition | Core destination/source/indeterminate decision and expanding-delay boundaries; warning-free build; full suite; deep-debug action/observation counters | Owner induces one missed navigation click and one lagged destination across Lobby navigation, Event stage, Repeat Stage, and Refuel; confirms the source action retries only after the destination fails, successful destinations do not trigger source OCR, and exhaustion returns to unattended recovery without blind clicks |
| Persistence/schema | Round trip, invalid data, atomic/collision behavior, full suite | Inspect migration outcome if existing owner data is involved |
| WPF layout/theme | Catalog counts, unique Light/Dark gradient definitions, complete live overlay resources, persistence migration/round trip, build, full suite, format | Visual check of both palette rows, selected outline/check, live switching, readable solid/gradient surfaces, and restart persistence in light and dark modes at relevant window sizes |
| Macro display profiles | Persistence/migration and layout-policy tests, per-runner RDP profile dimensions, WPF build, full suite | Owner assigns different resolutions to two runner rows; checks `1920 x 1080` full layout with dock, closes/reopens Roblox while Dock remains enabled and confirms automatic reacquisition, checks `1366 x 768` compact layout without dock, and verifies forced minimize/restore during a compact run |
| Capture/window/input | Pure geometry/work-area/protocol regression tests, including taskbar, offset-monitor, exact-width, too-small-work-area, and bounded pointer-acquisition retry constants; full suite | Owner moves canonical Roblox partly under the taskbar and beyond each monitor edge, runs an input-owning action, and confirms the full client is brought into usable view before input; during a long Route Optimizer batch, briefly move the physical mouse and confirm a transient positioning race retries without ending the batch; also validates sizing, focus, capture freshness, cancellation, and cleanup |
| Roblox and in-game startup settings normalization | Synthetic XML allowlist/preservation/malformed-input/atomic-recovery coverage; exact current-session process-name policy; panel ownership across `0.80`-`1.20`; semantic OCR without numeric text; feedback/clamp boundaries; per-session cache isolation; fixed option catalog, dominant toggle-color classification, Units endpoint geometry, supplied-dataset validation; warning-free build; full suite | Owner starts with changed UI/input values on the desktop and local-runner/RDP session, confirms Macro start closes Roblox, preserves unrelated XML, rejoins, converges rendered scale, closes/reopens Settings, corrects only the required option profile, closes Settings, and begins the task. Induce a later recovery rejoin and confirm neither in-game pass repeats; also run Runtime Lab `NORMALIZE STARTUP SETTINGS` explicitly. |
| Setup test playback | Bundled-map completeness/local-override tests, placement plan, phantom-versus-physical unit-panel policy, selected-panel dismissal policy, and input-protocol tests; warning-free build; full suite | Owner opens Setup from This desktop and a runner with no local datasets, confirms all release maps/views appear, then opens the selected map at Match Prestart without camera alignment or the canonical client size, clicks Test Setup, verifies automatic `1366 x 700` sizing, alignment, and complete before/Start/after playback, confirms phantom placements accept Targeting/Auto Upgrade/Reconfigure/Sell but not Upgrade, verifies Place configuration/Reconfigure/Upgrade clear the selected-unit panel before the next action while Sell closes it directly, and confirms Stop Test releases input |
| Team scrollbar A/B and fidelity test | Synthetic stable/moving/unstable thumb observations plus deterministic ramp-schedule boundaries, warning-free build, full suite | Owner opens Unit Teams, runs Drag or Scroll, confirms each reset reaches the top, and checks that the UI and `results.json` pair each requested wheel amount with its saved frames and measured normalized position |
| Team swap randomized test | Balanced randomized schedule coverage and seed determinism, translated-ROI client clipping, warning-free build, full suite | Owner opens Unit Teams, runs the chosen trial count, confirms each row reports the requested team, result, elapsed time, and terminal status, and verifies both Stop and F6 cancel without adding a failed trial |
| Calendar and shop utility tests | Route/catalog validation, item-selection persistence, reset-boundary policy, calendar relational geometry, warning-free build, full suite | Owner opens Runtime Lab Utility Tests from verified Lobby; runs Calendar Claim, Gold Shop, Raid Shop, and Expedition Shop independently, confirms the shop checklist reaches only selected items, unavailable rows are skipped, each flow respawns to verified Lobby, Stop cancels cleanly, and each operation produces a complete deep-debug archive |
| Deep debug | Archive entries, camel/snake-case JSONL parsing, redaction, input-marker coordinate translation, surface-specific retention, options persistence, single-owner behavior, full suite | Owner confirms Runtime Lab shows `DEEP DEBUG ON`, a run longer than 15 minutes retains its early frames, completed ZIPs open or drop into the dedicated viewer, nearby events align, click/scroll markers land correctly, and malformed entries fail visibly |
| Privacy, telemetry, and diagnostic upload | First-run notice-version persistence and independent choices; no pre-acceptance network start; online control/update gating; fixed telemetry schema, size/text bounds, exact endpoint, redirect rejection, no raw diagnostic strings; light-buffer frame/event/byte bounds and safe-field selection; stable random installation identity; trusted B2 origin/prefix and lookalike rejection; manual and automatic cleanup semantics; warning-free build; full suite | Owner reviews first-run defaults, saves each combination, and confirms Settings reflects it. With a disposable test endpoint, confirm telemetry produces only aggregate rows; trigger one light report with Deep Debug off and one archive with it on; confirm temporary light cleanup, successful Deep Debug cleanup, failed Deep Debug retention, and that all three disabled choices produce no covered requests. Never use an owner capture for staging validation |
| Signed control and active codes | Exact-schema/signature/freshness/revision/cache tests; feature-disablement and code-expiry policy; safe text alphabet/case mapping; bundled launcher/panel ROI uniqueness; destination-first transition policy; warning-free build; full suite | Owner publishes one mixed-case test code, starts each intended desktop/runner Macro from Lobby, confirms the launcher and panel are independently verified, exactly three Redeem actions occur, shared Areas cleanup returns to Lobby, and the same code is not retried until a manual Stop/Start. Publish a second code during an exact-task repeat and confirm the next terminal boundary returns through Lobby before repeating |
| Roblox dashboard dock | Style/exposure/maintenance/work-area policy tests, including tracked post-rejoin style repair and covered-dashboard reacquisition gating, warning-free build, full suite | Owner validates exact `1366 x 700` client size, interactive dock, macro input focus and post-rejoin redocking without alternating dock/undock; then covers the Macro with another app, focuses standalone Roblox, confirms it stays standalone, focuses the Macro, and confirms one stable redock. Also validates taskbar-safe maximize, undock restoration, tab/minimize behavior, and shutdown cleanup |
| OCR worker | Serialization/timing policy tests, exclusive response-handle release, narrow persistent-worker access classification, full suite | Owner validates installed CPU/GPU runtime and representative crops; runs a long Route Optimizer batch and confirms transient Windows response-file locks do not end collection |
| Macro terminal continuation | Exact-task Repeat Stage policy positive/negative matrix, run-scoped exact-team cache, warning-free build, full suite | Owner runs multi-win Story/Raid/Expedition/Event tasks and confirms the result clicks Repeat Stage, verifies fresh Match Prestart/Start Game evidence, and resumes without opening Teams or aligning the camera; confirm Expedition reruns route optimization and new-match placements. Select a different task using the same team and confirm Teams remains closed during that uninterrupted run, then manually stop/start and confirm team state begins unknown. Confirm reaching the target or selecting another task/act/mode resets through Lobby, and Challenge never repeats |
| Raid drop dismissal and unattended recovery | Raid Act 2/3 enablement negatives, bounded-point policy, retry/quarantine boundaries, warning-free build, full suite | Owner runs Raid Act 2/3 through a unit-obtainment overlay and confirms periodic bottom-right dismissal begins only after authored placement/configuration ends; then induces a recoverable timeout/failure and confirms Roblox restarts/rejoins, three failures quarantine the task, another task runs, and the quarantined task is reconsidered without stopping the Macro |
| Resource refuel tasks | Route identity/order, independent and combined schedule policy, exact field walk timing, 2/4/6-second station interaction policy, three-scale Add Fuel and Confirm/Cancel state ownership, three-anchor dialog geometry boundaries, Escape key input mapping, scheduler handoff, bundled-evidence validation, warning-free build, full suite | Owner configures combined Mine + Drill Refuel, then a match; confirms one startup normalization, Mine refuel and Esc/R/Enter Lobby return, Drill refuel and Lobby return, then direct match navigation. Verify each station tolerates delayed panel opening, Min/Max label changes do not affect the relational quantity click, a retained confirmation receives at most three fresh Confirm clicks, and the underlay alone cannot authorize completion. At the next terminal match boundary after 400 minutes, confirm the pair becomes due together, resets through Lobby, reruns Mine then Drill, and receives one new shared due time only after Drill succeeds. Also confirm separately authored Mine/Drill retain independent recurrence times. |
| Shop and Calendar utilities | Gold/Raid/Expedition independent allowlists, invalid/empty selection rejection, UTC-midnight plus two-day and seven-day beacon boundaries, available/gray button classification, purchase-dialog geometry, complete Calendar grid/reverse-order geometry, persistence/snapshot round trips, warning-free build, full suite | Owner enables several items in each shop, confirms dynamic scrolling buys only selected available rows and skips gray/Max Inventory rows, and verifies Esc/R/Enter Lobby handoff. Confirm Gold and Calendar wait until the next UTC midnight, Expedition until its two-day boundary, Raid until the displayed weekly boundary, and Calendar clicks Day 7 through Day 1 for three passes without targeting outside the live grid. |
| Expedition runtime | Node tracker real-Boss transitions, delayed-arrival policy, tooltip parsing negatives, dataset-owned Hover Line initial sweep, bottom-right hover-card clearing, cached marker-relative hover/local reacquisition, multi-scale marker location, same-marker color retention with mandatory cross-marker semantic reacquisition, Checkpoint-first live-control priority, repeated-source action replay, resource-scoped quantity normalization, reward-card association, Route Optimizer trial bounds, separate Encounter/Checkpoint source and confirmation ROI ownership, duplicate-label rejection, retained-source temporal retry, independent action/verification budgets, per-placement physical retention, current Start Game evidence, snapshot validation, warning-free build, full suite | Owner runs Route Optimizer for at least three trials at each UI scale and confirms every row reports the observed quantity, threshold decision, raw OCR, and elapsed time; confirms non-final trials reroll and the final trial remains at Match Prestart; then validates initial placement/immediate spawn Continue, first-node left-to-right hover calibration, pointer return to the bottom-right resting point before a Defense/Elite Start Game wait, later cached-offset node actions, extraction, idle reward-card dismissal, cancellation, and recovery. On successive Defense/Elite nodes, confirm a placement with no selection UI is skipped after its first physical-retention observation, while a replaced phantom remains eligible and receives saved configuration. For Encounter and a non-spawn Checkpoint, confirm node classification can occur while the ship moves without causing input, then verify Continue is clicked only after arrival exposes the source control. Complete a personalized color profile, move to a new marker, and verify tooltip semantics are reacquired before Boss/Assault history changes. Keep both live controls visible in a synthetic negative and verify the Checkpoint pair owns priority. Delay one confirmation click and confirm the modal action is retried from the modal, while a closed confirmation that returns to the source reopens after stable fresh evidence rather than succeeding or clicking the dimmed background control. After a fourth bounded action, verify the transition still observes its expected clear/destination without issuing a fifth click. |
| Discord webhook delivery | Stable/PTB/Canary official-host allowlist, lookalike-host rejection, mention suppression, shared success/error notification channel, secret-free transport/HTTP failures, full suite | Owner tests a webhook copied from each Discord client in use and confirms delivery through the bottom-right notification without exposing the URL in status or errors; Test Link uses the same-height action control and notification surface |
| Local instance manager contracts | Multi-profile manifest/identifier bounds, legacy single-runner migration, intentional zero-runner preservation, unique endpoints/credentials/tasks, shared-vs-isolated roots, profile allowlist, native-diagnostic classification, hash-cache invalidation, rollback/state transitions, secret redaction, installer policy, warning-free build, full suite | Provision only when the owner explicitly authorizes the current task; verify removing the final runner clears its row, two visible sessions, and full UI startup without computer-control tooling |
| Local instance integration | Disposable Windows 10/11 x64 VMs: clean install, legacy migration, Runner 1/Runner 2 concurrent UI startup, shared/separate configuration, repair, update, interruption, rollback, individual/all removal, uninstall, loopback isolation, ACL/SID checks, and capture/recovery matrix | Owner-authorized device testing may exercise setup/session lifecycle without UI automation; live Roblox behavior remains owner-operated |
| Coordinated application update | Exact tag/release/four-asset/digest/checksum policy tests, trusted-redirect tests, state/request round trips, installer-script checks, warning-free build, full suite | From an older signed Program Files build, owner downloads and installs a newer public signed release, confirms every open UI closes normally, active binaries are never overwritten, owner/runner UIs relaunch at the new version, and cancellation/tampering/private-release cases fail closed |
| Runner first launch and personalization | Standard-user versus exact elevated-hive policy ownership, protected-policy negative cases, trusted-download/signature tests, managed-instance mutex identity, warning-free build, full suite | Owner repairs an existing runner and creates a new runner, confirms setup does not fail at `Software\Policies`, owner desktop is unchanged, runner desktop is black with global icons hidden, Windows privacy and Edge first-run pages stay suppressed, official Roblox login opens, trusted installer starts only when Roblox is absent, no credential is copied or entered, and Repair does not duplicate an already-open runner UI |

The native preflight must run only in its sacrificial process and cache successful evidence by exact TermService and TermWrap hashes. Disposable VMs certify the end-to-end installer/session/rollback lifecycle, not a static build catalog. Failed required patches, ARM64, active remote use during first setup, non-loopback exposure, stale/black capture, wrong pipe peers, and incomplete cleanup must remain negative tests that fail closed.

## Manual handoff

When a change needs live verification, provide a concise checklist with expected outcomes. Do not claim the behavior passed until the owner reports it. Capture screenshots or logs only when the owner explicitly supplies them, and keep those artifacts outside the repository.

## Failure reporting

Report the exact command, exit code, and relevant error. Separate failures introduced by the current diff from pre-existing dirty-worktree or environment failures. Never weaken a check merely to produce a green run.
