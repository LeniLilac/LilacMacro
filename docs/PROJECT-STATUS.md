# Project status

**Status: Current as of 2026-08-11.** Update this matrix in the same change that moves a capability across a boundary.

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
| Plan page | Prototype | ExpeditionsMacro-parity task fields, compact draggable task blocks with Setup-matched preview/gap/insertion feedback, repeat-loop blocks, global priority, and centered task/loop editors persist atomically with the selected plan; scheduler integration remains incomplete |
| Full Settings page | Prototype | Press-then-key bindings, Plan state, visible-but-DPAPI-protected private-server link, masked DPAPI webhook, Discord failure options, full/compact display policy, coordinated update controls, and the local instance manager persist; private-server Test Link uses the registered Roblox protocol and Test Webhook sends a bounded mention-free Discord message, while runtime-triggered Discord delivery remains unwired |
| Local instance manager | Prototype | Exact-binary TermWrap preflight/cache, loopback isolation, per-runner standard accounts/credentials/ACLs/full-UI tasks, Runner 1..16 endpoints, shared-or-isolated configuration roots, runner-only black desktop/icon policy, trusted first-launch Roblox bootstrap, legacy single-runner migration, rollback, cleanup verification, and Settings setup/add/open/remove actions exist; disposable-VM lifecycle certification and broader owner live acceptance remain required |
| Signed installer foundation | Prototype | Inno Setup and build/validation/publish scripts produce one Program Files installer plus an exact four-asset release inventory, require release signing, coordinate all-UI shutdown/relaunch, preserve optional runner state across upgrade through repair, and block overwrite/uninstall on incomplete cleanup; no supported signed release exists |

## Accepted target architecture

| Capability | Status | Contract |
|---|---|---|
| Priority task scheduler | Prototype | Main Macro selects the lowest-numbered incomplete task after each terminal result; public unattended continuity, task quarantine, restart escalation, and delayed reconsideration remain Planned |
| Mandatory startup normalization | Prototype | At plan start and every private-server reset, each full Macro UI closes only its Windows session's Roblox client, atomically normalizes an explicit allowlist in that profile's Roblox settings, rejoins, verifies Lobby, and runs the bounded UI-scale feedback normalizer. Runtime Lab Wire Test shares rendered-scale normalization and Debug exposes it directly. Rendered panel geometry owns scale verification, cached values remain revalidated hints, and no disable controls exist; owner live acceptance remains required |
| Canonical Lobby reset | Prototype | Story/Raid main runs parse the DPAPI-protected configured private-server link into a bounded `roblox://` launch target, close/reopen Roblox without a browser intermediary, wait for verified Lobby, then reevaluate priority; indefinite scheduler-level recovery escalation remains Planned |
| Story runner | Prototype | Runtime Lab and main Macro share the complete navigation, authored placement, terminal, and reset path; owner live acceptance remains required |
| Raid runner | Prototype | Runtime Lab and main Macro share the complete navigation, authored placement, terminal, and reset path; owner live acceptance remains required |
| Challenge runner | Prototype | Main Macro and Runtime Lab share type rotation, random-map recognition, authored Challenge placement playback, terminal handling, Lobby return, and per-type half-hour/daily-limit eligibility; owner live acceptance remains required |
| Expedition runner | Planned | Map/difficulty selection, placement playback, terminal handling |
| Expedition reward reroll optimizer | Planned | A 414-frame standard-scale sample produced provisional per-resource thresholds, and one small-UI frame validated compact enlarged-ROI readability; broader post-update OCR accuracy, refreshed reward distributions, bounded reroll transitions, and owner live acceptance remain required |
| Event runner | Planned | Cross-scale evidence and owner-triggered Debug selection exist for Villain Invasion, Boss Bounty, and Guess That Unit; act selection, placement playback, and terminal handling remain planned |
| Tower runner | Unresolved | The updated Play UI dataset supports detecting and selecting Tower in Runtime Lab Debug, but no map, match, placement, or terminal flow has been specified |
| Placement playback | Prototype | Setup and Runtime Lab consume authored timelines with quick-place batching, physical/phantom selection proof, targeting, Auto Upgrade, reconfigure, upgrade, sell, and an auto-start-aware Start Game boundary that finishes prestart actions before continuing after-start; Runtime Lab additionally owns terminal handling, and owner live validation is still required |
| Personalized image-detection runtime | Planned | Decode selected datasets, OCR-bootstrap per-user profiles, run detector-first steady state, and use OCR fallback/refresh; Core fingerprinting and matching primitives are already implemented |
| Private-server links and webhooks | Prototype | The visible private-server field and masked webhook use DPAPI persistence; private-server protocol launch is bounded and validated, while webhook validation/delivery remains Planned |
| Packaging and releases | Prototype | Versioned local owner-test artifacts, strict public GitHub release metadata/asset/checksum policy, coordinated Program Files update shutdown/relaunch, and signed Inno Setup build/publish paths exist; production certificate ownership, anonymous release availability, and compatibility certification remain unresolved |

## Important non-equivalences

- A Debug click proves a bounded transition can be inspected; it does not implement a mode runner.
- A saved placement timeline proves authoring and validation; only an explicit Setup test, Runtime Lab run, or supported macro runner authorizes playback.
- OCR confidence is recorded evidence; it does not independently authorize unattended input.
- Direct `Repeat Stage` is retained Debug evidence; it is not the planned scheduler's terminal path.
- Dataset Builder and Runtime Lab are retained owner tools, not legacy code marked for removal.

See [Macro architecture](MACRO-ARCHITECTURE.md) for the planned runtime and [Game behavior](GAME-BEHAVIOR.md) for current Debug behavior.
