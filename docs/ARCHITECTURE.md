# Architecture

**Status: Current repository architecture.** Planned unattended runtime architecture lives in [Macro architecture](MACRO-ARCHITECTURE.md).

## Dependency direction

```text
LilacMacro.Core <- LilacMacro.Windows <- LilacMacro.Runtime <- LilacMacro.App
        ^                    ^                  ^                  ^
        +---------------- tests and tools consume lower layers ---+
```

- Core is platform-independent and owns deterministic contracts and policies.
- Windows owns Win32, Windows Graphics Capture, display geometry, hotkeys, and input.
- Runtime owns reusable WPF-free workflow composition shared by desktop and session execution.
- App owns WPF, lifecycle, user interaction, and service coordination.
- Tests may reference the layers they verify. The dataset tool consumes Core contracts and WPF imaging but does not own application workflow.

References must continue in this direction. A higher layer may adapt a lower-layer result; a lower layer must not learn about a view, page, or workflow runner.

## Core

Core owns:

- immutable pixel sizes, points, and half-open rectangles;
- timed/manual capture plans and deterministic schedules;
- dataset manifests, safe names, validation, and atomic persistence;
- OCR text normalization, target rules, state evaluation, and scale-relative layouts;
- grayscale adaptive-anchor fingerprints, bounded matching, declarative visual-state rules, and versioned profile persistence;
- physical key-sequence validation;
- placement map definitions, route inheritance, timeline contracts, validation, and atomic storage.

Core has no WPF, Win32, Direct3D, process-control, or user-specific default-path dependencies. Static policy can produce candidate coordinates, but only a live Windows/App caller can authorize input after revalidation.

## Windows

Windows enumerates visible top-level windows, admits Roblox-owned candidates, measures client-relative geometry, and resizes by correcting the observed difference between outer and client bounds. A resize succeeds only after the requested client dimensions are observed twice.

The Macro dashboard dock uses a verified top-level Roblox player window rather than process injection or an in-process game host. Windows records the original style, extended style, and outer bounds, removes the standalone frame, positions an interactive topmost cutout over the DPI-adjusted dashboard target, and verifies the resulting `1366 x 700` client. Undock restores the recorded style and bounds. The App layer owns visibility and lifecycle: tab changes, owner minimization or occlusion, failed verification, and shutdown release the cutout before the dashboard yields ownership.

Windows Graphics Capture owns fresh-frame acquisition. It keeps the source in 16-bit floating-point scRGB, identifies the Roblox monitor through DXGI, reads that display's current SDR white level through DisplayConfig, and normalizes Windows' HDR desktop white boost before tone mapping. One luminance shoulder compresses HDR highlights and one luminance-preserving gamut operation brings extended colors into sRGB; the old per-pixel brightest-channel normalization is not used. Encoded PNGs carry an explicit sRGB chunk. If display metadata is unavailable, conversion falls back to bounded standard-SDR behavior rather than failing capture. A changed surface stops the operation instead of returning a frame with assumed geometry.

Full dataset capture copies only the verified client rectangle into a compact GPU texture before CPU readback. The detector path accepts bounded client-relative rectangles, rejects invalid or excessive requests, shelf-packs them into one compact FP16 atlas, performs one GPU copy per rectangle and one atlas readback, then splits and converts only the requested pixels to grayscale. `VisualAnchorRegionMatcher` calculates the required search rectangle and translates ROI-local match results back into client coordinates. This path is implemented infrastructure; the unattended runtime that schedules profiles and authorizes input remains Planned.

Ordinary Win32 input is serialized through one nonblocking operation gate. The shared click protocol performs bounded focus attempts, acknowledged cursor motion, position settle, mouse down/up with release in `finally`, cursor parking, and a render settle. Scroll, bounded left-button drag, camera alignment, and physical key sequences use the same verified-window and cleanup boundaries. All points are client-relative until immediately converted through freshly observed bounds.

No Windows service may inject, read Roblox memory, hook the game, or bypass anti-cheat behavior.

## App surfaces

### Macro shell prototype

`MacroShellWindow` is the startup surface. It owns fixed browser-style Macro, Plan, Setup, and Settings tabs and preserves one page instance per tab.

- Macro is a right-rail runtime dashboard prototype. Its Start/Stop interaction changes local preview state only. Its explicit Dock/Undock control owns only verified Windows window placement and does not start scheduler input.
- Plan is an in-memory priority authoring prototype. It does not persist plans or feed a scheduler.
- Setup discovers finalized map datasets and owns placement authoring.
- Settings groups General, Roblox, Discord, keybind, and diagnostics controls behind internal category tabs. Theme and deep-debug diagnostics are connected. Keybinds persist in shared local app state and feed the global macro toggle, Story/Raid navigation, placement playback, and camera alignment; other controls remain session-only prototypes.
- Semantic color dictionaries can be replaced without rebuilding the visual tree.
- Central Lucide vector geometry owns icons, and one implicit thin-scrollbar style owns scrollable controls.

The first shell close request is canceled while queued placement writes flush. After a successful flush, the final close is queued onto the Dispatcher so it cannot re-enter WPF's active `Closing` event. A save failure leaves the window open and permits a later retry.

### Owner tool shells

`MainWindow` is shared WPF composition behind two dedicated executable launch modes. Each mode constructs only its owned pages:

- Dataset Builder owns Capture, Review, and Datasets: exact client sizing, timed/manual sampling, annotations, OCR trials, and finalized/recoverable dataset discovery.
- Runtime Lab owns Debug and Wire Test: explicit evidence checks, bounded input transitions, shared startup UI-scale normalization, and Story/Raid/Challenge chains with OCR or image-first evidence plus OCR fallback.

Both shells reuse `WorkspaceController`, OCR, vision, diagnostics, and Windows services. A cross-process file lease fails closed when a second LilacMacro process attempts Roblox input. Neither tool has an unattended loop. [Game behavior](GAME-BEHAVIOR.md) is the authoritative Debug state/action ledger.

## Deep debug boundary

App owns one process-wide deep-debug recorder because it coordinates WPF lifecycle, Workspace capture, OCR, vision, and input evidence. A bounded single-reader channel serializes events and already-acquired PNG bytes without requesting extra screenshots. The complete text timeline is always archived under `%LOCALAPPDATA%\LilacMacro\diagnostics`; Main Macro and Dataset Builder keep the configured final rolling image window, while Runtime Lab keeps already-acquired images for the complete owner-triggered operation. Both app surfaces consume the same format; Core and Windows do not depend on diagnostics. See [Deep debug](DEEP-DEBUG.md).

## OCR process boundary

OCR is an optional local Python helper rather than an in-process dependency. The setup script creates an isolated environment under `%LOCALAPPDATA%\LilacMacro\ocr` and installs one pinned CPU or GPU Paddle runtime plus PaddleOCR.

App writes one selected crop to a temporary PNG, invokes an allowlisted detector/recognizer pair on an allowlisted device, parses bounded JSON, shifts detected child boxes into original-frame coordinates, and removes the crop. One-shot mode exits per request. `KEEP LOADED` owns one child process and exchanges request/response files through a unique temporary channel with a hard deadline, worker-exit detection, and cleanup; the worker caches pipelines by model and device.

Review OCR is evidence. Debug input adds explicit state thresholds, a live target/layout requirement, fresh capture, and immediate Roblox revalidation. See [OCR and vision](OCR-AND-VISION.md).

## Startup normalization boundary

Runtime owns one UI-scale normalizer shared by desktop Macro, the headless local-session runtime, and Runtime Lab. OCR owns only the semantic Settings/search/UI Scale row structure used to authorize the value-field action. A bounded RGB detector independently measures Settings-panel geometry from the close control and three borders; the displayed scale number is neither parsed nor trusted. Candidate correction uses measured rendered scale, not device assumptions.

The successful numeric candidate is a disposable performance hint stored at `%LOCALAPPDATA%\LilacMacro\ui-scale-calibration.json`. `%LOCALAPPDATA%` isolates Windows users, and entries are keyed by the current Windows session id so the console and separate RDP sessions cannot silently share a value. Every use remeasures rendered geometry, stale entries fall into the same bounded feedback loop, invalid or unreadable cache data behaves as a miss, and cache-write failure does not weaken runtime verification.

## Adaptive visual-anchor boundary

Core owns platform-independent grayscale rasters, burst fingerprint construction, temporal reliability masks, bounded template matching, profile manifests, immutable revisions, and declarative state composition. Profiles are element data: ids, OCR aliases, generated rasters, metrics, and click-point policy. Core contains no per-button detector implementations.

App or a future Vision adapter will decode captures, supply verified OCR bounds, select a profile root, and present evidence. Automation may consume a match only after its state owner and live-input policy independently revalidate the current Roblox window and target. The implemented matcher is therefore evidence infrastructure, not click authorization.

## Dataset and tool boundary

Datasets are self-contained directories governed by [Dataset format](DATASET-FORMAT.md) and the normative JSON Schema. Draft creation, image writes, manifest updates, and finalization avoid partial or colliding output.

`LilacMacro.DatasetTool` validates a dataset before creating any derived view. Agent views contain bounded chronological contact sheets, annotation crops, OCR maps, JSONL, an index, and a summary. Output defaults to an ignored dataset-local `.agent-view` directory and is never overwritten. See [Agent dataset workflow](AGENT-DATASET-WORKFLOW.md).

## Placement boundary

Core owns placement documents and validation. App owns map discovery, gallery state, route editing, map-coordinate transforms, timeline docking/popout, and a serialized autosave queue. Saved documents live under `%LOCALAPPDATA%\LilacMacro\placements`; source map images remain in the dataset root.

Authoring and explicit owner-triggered playback through Setup and Runtime Lab are Prototype. Unattended scheduler integration remains Planned. See [Placement authoring](PLACEMENT-AUTHORING.md).

## Persistence and trust boundaries

- Dataset images and annotations stay under the owner-selected dataset root.
- Non-secret capture settings, per-session UI-scale calibration hints, placements, OCR runtime, crash logs, and deep-debug archives stay under the current Windows profile.
- All committed document-style writes validate first and use temporary-file replacement.
- Local paths, captures, models, logs, settings, and agent views remain outside Git.
- Private-server links and webhook URLs are not implemented. Their planned DPAPI boundary is documented in [Privacy](../PRIVACY.md).

## Optional local-session boundary

The experimental local runner preserves the same layer direction. Core owns versioned status, manifest, profile-policy, snapshot, command, event, transition, and validation contracts. Windows owns account, Credential Manager, ACL, TermService, firewall, scheduled-task, session, capture-freshness, rollback, and named-pipe transport adapters. App owns Settings actions, execution-target selection, UAC helper launch, connection lifecycle, and user-visible health.

`LilacMacro.SessionSetup.exe` is the only elevated component and accepts only `install`, `repair`, `remove`, and `uninstall-cleanup`. `LilacMacro.SessionWorker.exe` is windowless and runs inside the dedicated standard account. `LilacMacro.Runtime` owns the WPF-free Story/Raid/Challenge scheduler and links the same workflow, OCR, placement, terminal, and rejoin policies used by the desktop macro. The desktop controller sends typed immutable snapshots and declarative commands; it never forwards raw input across sessions. A lost or invalid pipe cancels work and releases input ownership.

Runtime readiness is separate from provisioning and fresh capture. The worker promotes the runner to Ready only after the shared runtime is available and a fresh WGC frame is verified inside the visibly connected runner session. Native compatibility is established by the bundled scanner against the exact installed TermService binary, then cached by binary hashes; disposable-VM certification still owns installer, native-session, rollback, and removal acceptance. See [Optional local runner session](LOCAL-SESSION.md).

## Planned runtime boundary

The priority scheduler, private-server Lobby reset, shared team/navigation/match modules, placement playback, adaptive-anchor runtime integration, and terminal outcome loop are Planned. They must extend this layering rather than turn Debug code or WPF pages into a monolithic runner. See [Project status](PROJECT-STATUS.md) and [Macro architecture](MACRO-ARCHITECTURE.md).
