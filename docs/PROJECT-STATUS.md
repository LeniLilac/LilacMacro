# Project status

**Status: Current as of 2026-08-10.** Update this matrix in the same change that moves a capability across a boundary.

The [documentation contract](../CONTRIBUTING.md#documentation-contract) defines the status terms. In short: **Implemented** means production capability exists, **Prototype** means usable but internal or incomplete code exists, **Planned** means accepted design intent, and **Unresolved** means a decision remains open.

## Current repository

| Capability | Status | Evidence and boundary |
|---|---|---|
| Layered .NET solution | Implemented | `Core <- Windows <- Runtime <- App`, with tests and tools consuming shared lower-layer contracts |
| Dataset manifest, validation, hashing, drafts, and finalization | Implemented | Schema version 1, atomic JSON writes, collision-free final directories |
| Timed and manual Roblox frame capture | Implemented | Dataset Builder; F6 starts independent timed drafts and F5 appends manual frames |
| Frame review and annotations | Implemented | Verdicts, notes, local/global coarse boxes, per-result OCR/image relevance, exact/fuzzy matching, inferred/overridden spatial selectors, declarative required plus N-of-pool evidence, and atomic manifest updates |
| Agent dataset views | Implemented | Validation-first contact sheets, crops, OCR maps, JSONL, index, and summary |
| PP-OCRv6 detection and recognition trials | Implemented | Small/tiny pairs, CPU/GPU runtime selection, timing, cached worker, and coordinates |
| Roblox discovery, exact client sizing, and fresh capture | Implemented | Ordinary Windows APIs and FP16 Windows Graphics Capture with display-aware HDR-to-sRGB conversion, client-only GPU crop, and tagged PNG output |
| Detector ROI capture pipeline | Implemented | Bounded client-relative regions, compact GPU atlas, one readback, deterministic grayscale conversion, and ROI-to-client match translation; Wire Test schedules diagnostic captures but unattended scheduling is absent |
| Roblox-compatible input protocol | Implemented | Bounded focus, acknowledged cursor motion, click, scroll, camera, and key release behavior |
| OCR normalization and layout policies | Implemented | Deterministic exact/fuzzy phrase and layout policies with automated tests; Team Swap ignores customizable names, calibrates deterministic wheel travel from a live thumb probe, waits through bounded list easing, verifies the middle landing, and uses relational Save/Load rows |
| Adaptive visual-anchor foundation | Implemented | Generic burst fingerprint builder, temporal reliability masks, bounded grayscale/edge matcher, visible Review evidence, declarative state rules, hashed PGM assets, and immutable profile revisions in Core |
| Dataset Builder and Runtime Lab split | Implemented | Dedicated launch modes construct only Capture/Review/Datasets or Debug/Wire Test/Scroll Test/Team Swap Test while reusing one App/Core/Windows implementation and a cross-process Roblox-input lease |
| Browser-style Macro/Plan/Setup/Settings shell | Prototype | Current startup surface; page instances survive tab changes |
| Roblox dashboard docking | Prototype | Macro can pin a verified Roblox player into a DPI-aware `1366 x 700` live surface; docking is suspended while LilacMacro is behind another application, and original frame/bounds are restored on undock, tab change, minimize/occlusion, failure, or shutdown; live behavior remains owner-validated |
| Setup map gallery and placement timeline authoring | Prototype | Functional local authoring, route inheritance/reset, Team/Unit selectors with `1`-`6` keyboard shortcuts, explicit Place/Select cursor modes, compact pin markers, selection-only stable-ID drag/delete actions, centered step editors, tabs, popout, and autosave; guaranteed authored prestart delays are capped at 30000 ms; Test Setup flushes the active route, aligns the camera, and executes its complete timeline from Match Prestart, while Match Settings controls remain unwired |
| Light/dark mode | Prototype | Session-only Settings toggle |
| Debug state transitions | Prototype | Runtime Lab state checks can run OCR or saved-image-first with OCR fallback/profile refresh; click actions still obtain fresh OCR bounds, and there is no unattended loop or automatic retry |
| Story/Raid/Challenge Wire Test | Prototype | Owner-triggered, cancellable Lobby-to-Match-Prestart navigation can run pure OCR or image-first state waits with OCR fallback/profile refresh; Challenge adds type rotation, availability/cooldown evidence, and reset-scoped eligibility; an explicit option continues through the authored placement route, Start Game, and terminal verification |
| Team scrollbar A/B and fidelity test | Prototype | Runtime Lab can independently repeat the existing midpoint drag or ramp wheel units by a per-trial increment from the top clamp, save full before/after evidence, pair each requested amount with a normalized thumb position, and write variance results; this owner test does not change production team selection |
| Randomized team swap test | Prototype | Runtime Lab can run balanced randomized Teams 1-8 through the shared production swap path, retain calibration across trials, continue after ordinary failures, and expose target, result, time, and terminal status; owner live acceptance remains required |
| Fast unit playback evidence | Prototype | Quick-place batches use shared input ownership; initial Priority/Sell/DPS OCR calibrates live panel geometry, later checks use compact RGB ROIs, physical DPS confirmation rejects phantom selections, and green/normal-gray/expanded-gray Upgrade states are classified without full-frame OCR |
| Deep debug diagnostics and viewer | Implemented | Main Macro, Dataset Builder, and Runtime Lab share bounded JSONL/timeline recording, redaction, crash finalization, agent contact sheets, and local ZIP retention; Main Macro/Dataset Builder retain a rolling PNG window, Runtime Lab retains its complete owner-triggered operation, and the dedicated viewer streams ZIP frames, synchronizes nearby events, and overlays client-relative click/scroll evidence |
| Macro page | Prototype | Right-rail dashboard with plan selection, start/stop preview state, dockable Roblox surface, scheduler inspection, Lobby reset evidence, totals, and run log; no scheduler runtime is connected |
| Plan page | Prototype | ExpeditionsMacro-parity task fields, compact draggable task blocks, repeat-loop blocks, global priority, and centered task/loop editors are available in memory; persistence and scheduler integration are absent |
| Full Settings page | Prototype | General, Roblox, Discord, and diagnostics categories remain mostly session-only; press-then-key bindings persist atomically across versioned artifacts and drive global macro start/stop, Story/Raid navigation, placement actions, and camera shift lock, while live theme switching plus persisted deep-debug enablement, retention, and folder access are connected |
| Optional local runner session | Prototype | Versioned contracts, offline exact-binary TermWrap self-scan with hash-bound cache, runner-only profile policy, account/credential/ACL/firewall/task ownership, rollback journal, typed immutable snapshots, named-pipe peer validation, cleanup verification, Settings status/actions, execution-target persistence, and the shared headless Story/Raid/Challenge runtime exist; disposable-VM lifecycle certification and owner live acceptance remain required |
| Signed installer foundation | Prototype | Inno Setup definition and build/validation scripts produce one Program Files installer, require release signing, preserve optional runner state across upgrade through repair, and block uninstall on incomplete cleanup; no supported signed release exists |

## Accepted target architecture

| Capability | Status | Contract |
|---|---|---|
| Priority task scheduler | Prototype | Main Macro selects the lowest-numbered incomplete task after each terminal result; public unattended continuity, task quarantine, restart escalation, and delayed reconsideration remain Planned |
| Mandatory startup normalization | Planned | Every public run must normalize required Roblox settings and the standard 100% UI scale, then obtain fresh Lobby evidence; these invariants have no user-facing disable controls |
| Canonical Lobby reset | Prototype | Story/Raid main runs open the session-configured HTTPS roblox.com private-server link, wait for verified Lobby, then reevaluate priority; DPAPI persistence and indefinite scheduler-level recovery escalation remain Planned |
| Story runner | Prototype | Runtime Lab and main Macro share the complete navigation, authored placement, terminal, and reset path; owner live acceptance remains required |
| Raid runner | Prototype | Runtime Lab and main Macro share the complete navigation, authored placement, terminal, and reset path; owner live acceptance remains required |
| Challenge runner | Prototype | Main Macro and Runtime Lab share type rotation, random-map recognition, authored Challenge placement playback, terminal handling, Lobby return, and per-type half-hour/daily-limit eligibility; owner live acceptance remains required |
| Expedition runner | Planned | Map/difficulty selection, placement playback, terminal handling |
| Expedition reward reroll optimizer | Planned | A 414-frame standard-scale sample produced provisional per-resource thresholds, and one small-UI frame validated compact enlarged-ROI readability; broader post-update OCR accuracy, refreshed reward distributions, bounded reroll transitions, and owner live acceptance remain required |
| Event runner | Planned | Cross-scale evidence and owner-triggered Debug selection exist for Villain Invasion, Boss Bounty, and Guess That Unit; act selection, placement playback, and terminal handling remain planned |
| Tower runner | Unresolved | The updated Play UI dataset supports detecting and selecting Tower in Runtime Lab Debug, but no map, match, placement, or terminal flow has been specified |
| Placement playback | Prototype | Setup and Runtime Lab consume authored timelines with quick-place batching, physical/phantom selection proof, targeting, Auto Upgrade, reconfigure, upgrade, sell, and an auto-start-aware Start Game boundary that finishes prestart actions before continuing after-start; Runtime Lab additionally owns terminal handling, and owner live validation is still required |
| Personalized image-detection runtime | Planned | Decode selected datasets, OCR-bootstrap per-user profiles, run detector-first steady state, and use OCR fallback/refresh; Core fingerprinting and matching primitives are already implemented |
| Private-server links and webhooks | Planned | Settings, DPAPI protection, redaction, and bounded use |
| Packaging and releases | Unresolved | Versioned local owner-test artifacts are Implemented and the signed Inno Setup path is Prototype; production certificate ownership, compatibility certification, update channel, and supported release workflow remain unresolved |

## Important non-equivalences

- A Debug click proves a bounded transition can be inspected; it does not implement a mode runner.
- A saved placement timeline proves authoring and validation; only an explicit Setup test, Runtime Lab run, or supported macro runner authorizes playback.
- OCR confidence is recorded evidence; it does not independently authorize unattended input.
- Direct `Repeat Stage` is retained Debug evidence; it is not the planned scheduler's terminal path.
- Dataset Builder and Runtime Lab are retained owner tools, not legacy code marked for removal.

See [Macro architecture](MACRO-ARCHITECTURE.md) for the planned runtime and [Game behavior](GAME-BEHAVIOR.md) for current Debug behavior.
