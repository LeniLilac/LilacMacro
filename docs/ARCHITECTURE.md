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

Windows enumerates visible top-level windows, admits Roblox-owned candidates, measures client-relative geometry, and resizes by correcting the observed difference between outer and client bounds. A resize succeeds only after the requested client dimensions are observed twice. Before every input operation, Windows resolves Roblox's current monitor and requires 96-by-96 effective DPI, which is 100% Windows display scale. A mismatch fails before input with the detected percentage and asks the owner to change that monitor to 100% and restart Roblox. Windows also fits the complete client rectangle inside the nearest monitor's usable work area and verifies the result; window borders may remain outside that area when the client exactly matches the monitor width, but no client-relative target may land behind a taskbar or beyond a monitor edge.

The Macro dashboard dock uses a verified top-level Roblox player window rather than process injection or an in-process game host. Windows records the original style and extended style, removes the standalone frame, positions an interactive topmost cutout over the DPI-adjusted dashboard target, and verifies the resulting `1366 x 700` client. Undock and every automatic dock release restore the standalone styles at the last docked position and size, leaving Roblox available for owner repositioning or resizing. When Dock remains requested but the dashboard becomes unavailable through a tab change, owner minimization, or foreign-window focus, the cutout is released and Roblox is minimized; if Roblox itself owns foreground focus, it is restored and left visible instead. Returning focus to an exposed Macro dashboard reacquires once, and a verified process-replacing runtime rejoin explicitly signals the surface to acquire the new Roblox window. Failed verification and shutdown release the cutout before the dashboard yields ownership.

Windows Graphics Capture owns fresh-frame acquisition. It keeps the source in 16-bit floating-point scRGB, identifies the Roblox monitor through DXGI, reads that display's current SDR white level through DisplayConfig, and normalizes Windows' HDR desktop white boost before tone mapping. One luminance shoulder compresses HDR highlights and one luminance-preserving gamut operation brings extended colors into sRGB; the old per-pixel brightest-channel normalization is not used. Encoded PNGs carry an explicit sRGB chunk. If display metadata is unavailable, conversion falls back to bounded standard-SDR behavior rather than failing capture. A changed surface stops the operation instead of returning a frame with assumed geometry.

Full dataset capture copies only the verified client rectangle into a compact GPU texture before CPU readback. The detector path accepts bounded client-relative rectangles, rejects invalid or excessive requests, shelf-packs them into one compact FP16 atlas, performs one GPU copy per rectangle and one atlas readback, then splits and converts only the requested pixels to grayscale. `VisualAnchorRegionMatcher` calculates the required search rectangle and translates ROI-local match results back into client coordinates. This path is implemented infrastructure; the unattended runtime that schedules profiles and authorizes input remains Planned.

Ordinary Win32 input is serialized through one nonblocking operation gate. The shared click protocol performs work-area containment, bounded focus attempts, acknowledged cursor motion, position settle, mouse down/up with release in `finally`, cursor parking, and a render settle. Scroll, bounded left-button drag, camera alignment, and physical key sequences use the same verified-window and cleanup boundaries. All points are client-relative until immediately converted through freshly observed bounds.

No Windows service may inject, read Roblox memory, hook the game, or bypass anti-cheat behavior.

## App surfaces

### Macro shell prototype

`MacroShellWindow` is the startup surface. It owns fixed browser-style Macro, Plan, Setup, and Settings tabs and preserves one page instance per tab.

- Macro is a right-rail runtime dashboard prototype. Its Start/Stop interaction changes local preview state only. Its explicit Dock/Undock control owns only verified Windows window placement and does not start scheduler input.
- Plan is an in-memory priority authoring prototype. It does not persist plans or feed a scheduler.
- Setup discovers finalized map datasets and owns placement authoring.
- Settings groups General, Roblox, Discord, keybind, and diagnostics controls behind internal category tabs. Theme, the versioned privacy choices, deep-debug diagnostics, and automatic error reports are connected; manual diagnostic uploads are absent. The first Macro launch blocks all choice-covered requests until the current notice is durably saved. A separately atomic `privacy-choices.json` owns a monotonic consent generation across owner and shared-runner processes; covered sends recheck that record, generation changes fail closed for stale processes, and ordinary settings saves merge rather than overwrite it. Local opt-outs revoke immediately, opt-ins activate only after a successful atomic write, and failed writes remain visibly unsaved and fail closed. The same three choices remain editable in General settings.
- Semantic color dictionaries can be replaced without rebuilding the visual tree.
- Central Lucide vector geometry owns icons, and one implicit thin-scrollbar style owns scrollable controls.
- Plan sharing is an explicit Online-features action. App validates and Brotli-compresses a closed bundle containing only the selected Plan and/or supported placement documents, then treats the returned short code as a bearer capability. Import validates size, decompression, schema, Plan policy, map identity, and placement rules before any local mutation. A shared file lease stored inside the active configuration root excludes imports while any owner or separate-account shared runner uses that configuration; staged placement writes and the Plan save roll back together when a commit fails.
- Discord runtime events make one bounded current-Roblox-client PNG capture at the event boundary before the next workspace operation or Roblox reset can begin. Capture waits up to one second for a transient shared-workspace owner such as the deep-debug sampler, then a bounded single-reader background queue handles network delivery using official Components V2 containers. Capture failure falls back to a text-only event without recapturing a later game state; the Test webhook path is always text-only. Event-specific settings gate run starts/stops, task changes, results, recovery attempts, and terminal failures; only the terminal-failure path can allowlist the configured Discord user ID for a mention.

The first shell close request is canceled while queued placement writes flush. After a successful flush, the final close is queued onto the Dispatcher so it cannot re-enter WPF's active `Closing` event. A save failure leaves the window open and permits a later retry.

### Owner tool shells

`MainWindow` is shared WPF composition behind two dedicated executable launch modes. Each mode constructs only its owned pages:

- Dataset Builder owns Capture, Review, and Datasets: exact client sizing, timed/manual sampling, annotations, OCR trials, and finalized/recoverable dataset discovery.
- Runtime Lab owns Debug, Wire Test, and bounded trial tools: explicit evidence checks, bounded input transitions, owner-triggered startup settings normalization, supported mode chains, and owner-triggered Team Scroll, Team Swap, and Expedition Route Optimizer batches. Route Optimizer reuses the production compact reward OCR and restart transition while preserving explicit cancellation and fresh input ownership.

Both shells reuse `WorkspaceController`, OCR, vision, diagnostics, and Windows services. A cross-process file lease fails closed when a second LilacMacro process attempts Roblox input. Neither tool has an unattended loop. [Game behavior](GAME-BEHAVIOR.md) is the authoritative Debug state/action ledger.

## Deep debug boundary

App owns one process-wide deep-debug recorder because it coordinates WPF lifecycle, Workspace capture, OCR, vision, and input evidence. A bounded single-reader channel serializes events and PNG bytes. Each active surface supplies one full-client sample per fixed one-second interval. The recorder holds only a ten-second pre-error buffer, preserves ten seconds after classified failures, merges overlapping windows, and keeps one representative frame for important transitions. Deterministic error signatures plus coarse perceptual hashes prioritize terminal, first-signature, visually distinct, recent, and repeated evidence under a 2.5 GiB frame target and 3 GiB archive hard limit. After local-instance provisioning, all owner/runner processes archive into ACL-protected `%ProgramData%\LilacMacro\Diagnostics` and enforce one atomic machine-wide byte budget; unprovisioned profiles use `%LOCALAPPDATA%\LilacMacro\diagnostics`. App selects the storage root, Windows provisions its ACL, and Core remains independent of diagnostics. See [Deep debug](DEEP-DEBUG.md).

Diagnostic transport remains separate from Deep Debug capture. Core owns the single 3 GiB archive limit, deterministic filename and kind validation, and trusted-storage policy. Runtime owns the WPF-free HTTPS, SHA-256, and multipart protocol plus a random installation-identity store. App owns the default-on automatic-error-upload choice, consent-generation cancellation, and lifecycle disposal. Only completed Deep Debug archives containing classified failures are submitted; there is no light archive or in-memory report buffer. Disabling Deep Debug or entering its low-disk pause also pauses automatic uploads. A successfully accepted archive is deleted, a failed upload remains local, and exact-name deletion markers retry a Windows-locked successful deletion. The app has no manual diagnostic file picker or upload control.

Product telemetry is a separate fixed-schema path. Core owns a discriminated per-kind allowlist and realistic value bounds; Runtime owns a redirect-disabled HTTPS transport to the exact official endpoint plus a small failure rate-limit store; App maps an allowlist of non-frame process observations into bounded events and holds at most 256 in memory. OCR setup failures use classified code/stage/device/runtime flags, and local-instance failures use classified operation/status/mode fields; neither carries setup output, account names, profile identifiers, endpoints, or exception text. The client ledger marks a failure code/scope/version only after successful delivery; it is not a disk retry queue. Telemetry never includes arbitrary diagnostic data or causes PNG cloning, and the versioned telemetry choice gates collection and delivery independently from online control and diagnostics.

## Services control boundary

Core owns the signed control-snapshot schema, Ed25519 verification, freshness/revision rules, feature identifiers, public code filtering, and schedule calculations. Runtime owns the bounded no-redirect HTTPS transport, atomic last-known-good cache, and jittered polling. Each full Macro UI, including every managed runner desktop, polls independently and accepts only a fresh snapshot signed by a release-bundled public key. An unavailable service therefore cannot inject new state; a still-fresh verified cache may continue, while expiry removes the snapshot from runtime decisions. A fresh signed shop schedule is authoritative for its exact named reset and may move the next occurrence earlier or later than the bundled fallback beacon; when no fresh schedule exists, the local field-derived cadence remains in force.

App maps the verified snapshot into scheduler policy. Game maintenance can prevent a new run or stop safely at a scheduler boundary; feature disablements skip only their named task/action; public game codes are redeemed from verified Lobby through separate dataset-owned launcher and panel states. Code input uses ordinary case-sensitive Windows keyboard events, each published code is attempted once per uninterrupted user-started Macro run, and a newly published code forces the next terminal boundary through Lobby instead of bypassing it with Repeat Stage. No Services command receives direct Windows-input or process authority.

## OCR process boundary

OCR is an optional local Python helper rather than an in-process dependency. The setup script creates an isolated environment under `%LOCALAPPDATA%\LilacMacro\ocr` and installs one pinned CPU or GPU Paddle runtime plus PaddleOCR. CPU workers disable MKL-DNN/oneDNN for the bundled PP-OCRv6 path because the bundled CPU build has an unsupported detection attribute in that backend; GPU workers retain their accelerated path.

App writes one selected crop to a temporary PNG, invokes an allowlisted detector/recognizer pair on an allowlisted device, parses bounded JSON, shifts detected child boxes into original-frame coordinates, and removes the crop. One-shot mode exits per request. `KEEP LOADED` owns one child process and exchanges request/response files through a unique temporary channel with a hard deadline, worker-exit detection, and cleanup; the worker caches pipelines by model and device.

Review OCR is evidence. Debug input adds explicit state thresholds, a live target/layout requirement, fresh capture, and immediate Roblox revalidation. See [OCR and vision](OCR-AND-VISION.md).

Transition layers remain separate evidence owners. A source control and the confirmation modal or destination it opens each load their own dataset-labeled ROI and semantic rule; a runtime service must not union those rectangles or choose among duplicate labels by global raster order. Destination evidence is evaluated first. Only a destination miss permits a fresh source observation, and a retained source may retry only through that source state's own live action bounds. This prevents a dimmed background control from impersonating a modal action and makes temporal rollback observable instead of treating modal disappearance as success.

## Runtime evidence boundary

Every semantic runtime search area is traceable to one named annotation in the curated evidence bundle. App owns `DebugStateSpec` annotation selection and `RuntimeSearchRegionEvidenceCatalog`; Runtime consumes the resulting WPF-free rectangles and state contracts. Static search rectangles are centralized and compared against their bundled annotations by policy tests. Dynamic search geometry must start from freshly observed bounds owned by a bundled state. Full-client capture transports pixels but does not itself establish semantic ownership. The bundle allowlist, synchronization process, privacy exception, and ambiguity stop rule are defined in [Runtime evidence ownership](RUNTIME-EVIDENCE.md).

Release installations do not contain the runtime evidence datasets or their source frames. A deterministic repository check derives the minimal ROI and visual-anchor context catalog from those datasets, verifies it is current, and embeds that compact metadata in the application assembly. Full manifests and frames remain repository/test evidence. Placement map JPGs are the only screenshot assets copied into application output; installer upgrades remove the legacy `Assets\RuntimeEvidence` directory created by earlier development builds.

## Startup normalization boundary

Core owns the exact allowlist and structural XML policy for Roblox's per-profile `UserGameSettings`. Windows owns current-session Roblox process shutdown and the bounded, atomic `GlobalBasicSettings_13.xml` replacement. The file is never edited while a Roblox client in that Windows session remains alive. The policy fails closed on a missing, malformed, duplicated, or type-changed required field, preserves all unrelated XML, keeps a transient sibling rollback file only across replacement verification, and never edits another Windows profile.

At desktop and local-runner plan start and after every terminal private-server reset, Runtime composes shutdown, Roblox XML normalization, validated private-server launch, and fresh Lobby verification in that order. Once per explicit Macro start, the first verified Lobby additionally receives rendered UI-scale normalization, a close/reopen boundary, and fixed in-game option normalization. Recovery attempts preserve that completed startup state so an operational anomaly does not repeat the in-game passes.

Runtime owns one rendered UI-scale normalizer shared by every desktop Macro instance and Runtime Lab. OCR owns only the semantic Settings/search/UI Scale row structure used to authorize the value-field action. A bounded RGB detector independently measures Settings-panel geometry from the close control and three borders; the displayed scale number is neither parsed nor trusted. Candidate correction uses measured rendered scale, not device assumptions.

Runtime also owns a positional in-game option normalizer for the post-scale canonical panel. It verifies tab selection, green/red toggle surfaces, and the Units scrollbar endpoint before input; it never OCRs the individual option labels or treats static points alone as authority. Runtime Lab exposes the complete sequence explicitly, while production invokes it only at Macro start.

The successful numeric candidate is a disposable performance hint stored at `%LOCALAPPDATA%\LilacMacro\ui-scale-calibration.json`. `%LOCALAPPDATA%` isolates Windows users, and entries are keyed by the current Windows session id so the console and separate RDP sessions cannot silently share a value. Every use remeasures rendered geometry, stale entries fall into the same bounded feedback loop, invalid or unreadable cache data behaves as a miss, and cache-write failure does not weaken runtime verification.

## Adaptive visual-anchor boundary

Core owns platform-independent grayscale rasters, burst fingerprint construction, temporal reliability masks, bounded template matching, profile manifests, immutable revisions, and declarative state composition. Profiles are element data: ids, OCR aliases, generated rasters, metrics, and click-point policy. Core contains no per-button detector implementations.

App or a future Vision adapter will decode captures, supply verified OCR bounds, select a profile root, and present evidence. Automation may consume a match only after its state owner and live-input policy independently revalidate the current Roblox window and target. The implemented matcher is therefore evidence infrastructure, not click authorization.

## Dataset and tool boundary

Datasets are self-contained directories governed by [Dataset format](DATASET-FORMAT.md) and the normative JSON Schema. Draft creation, image writes, manifest updates, and finalization avoid partial or colliding output.

`LilacMacro.DatasetTool` validates a dataset before creating any derived view. Agent views contain bounded chronological contact sheets, annotation crops, OCR maps, JSONL, an index, and a summary. Output defaults to an ignored dataset-local `.agent-view` directory and is never overwritten. See [Agent dataset workflow](AGENT-DATASET-WORKFLOW.md).

## Placement boundary

Core owns placement documents and validation. App owns map discovery, gallery state, route editing, map-coordinate transforms, timeline docking/popout, and a serialized autosave queue. Saved documents live under `%LOCALAPPDATA%\LilacMacro\placements`; source map images remain in the dataset root.

Authoring and playback through Setup, Runtime Lab, the desktop scheduler, and managed-session runtime are Prototype. Each runtime owns the complete placement lease and cancellation boundary. See [Placement authoring](PLACEMENT-AUTHORING.md).

## Persistence and trust boundaries

- Dataset images and annotations stay under the owner-selected dataset root.
- Non-secret capture settings, per-session UI-scale calibration hints, placements, OCR runtime, and crash logs stay under the current Windows profile. Deep Debug archives move to the protected machine pool only after local-instance provisioning. The macro edits only its documented allowlist in the current profile's Roblox global settings immediately before a private-server launch.
- All committed document-style writes validate first and use temporary-file replacement.
- Local paths, captures, models, logs, settings, and agent views remain outside Git.
- Private-server links and webhook URLs use DPAPI protection at rest and are redacted from diagnostics. The private-server field is intentionally visible while editing; the webhook remains masked. Webhook delivery accepts only HTTPS URLs on Discord's stable, PTB, or Canary hosts with the canonical `/api/webhooks/{id}/{token}` path, rejects redirects, and suppresses all unconfigured mentions. See [Privacy](../PRIVACY.md).

## Optional local-session boundary

The local instance manager preserves the same layer direction. Core owns versioned status, manifest, runner-profile, configuration-mode, transition, and validation contracts. Windows owns accounts, Credential Manager, ACLs, TermService, firewall, scheduled tasks, session inspection, and rollback. App owns the instance list, UAC helper launch, per-process configuration context, and user-visible health.

`LilacMacro.SessionSetup.exe` is the only elevated component and accepts only bounded machine/profile verbs. Every runner task starts the installed full `LilacMacro.exe` UI in that standard account on logon or reconnect; macro capture, input, settings normalization, OCR, and recovery therefore remain local to the owning desktop. `LilacMacro.SessionWorker.exe` is retained only for its one-shot profile-policy bootstrap during setup/repair. Legacy IPC/snapshot contracts are not on the active execution path.

Machine readiness means exact-binary compatibility, loopback isolation, account/profile ACLs, endpoint credentials, and full-UI tasks are installed. Roblox/capture readiness belongs to each visible macro UI at run time, exactly as on the main desktop. Native compatibility is cached by exact binary hashes; disposable-VM certification still owns installer, multi-session, rollback, and removal acceptance. See [Local instance manager](LOCAL-SESSION.md).

The owner UI coordinates updates rather than copying binaries into live runner profiles. Core validates one exact six-asset GitHub release contract and the embedded Ed25519 release-trust key; App performs bounded exact-origin downloads, GitHub digest checks, detached project-signature verification, checksum comparison, and records the exact macro PIDs and runner profiles to stop. The intentionally non-Authenticode installer requests ordinary shutdown from every UI, refuses to overwrite an active or uninspectable process, installs once under Program Files, then the allowlisted elevated helper re-registers and launches every configured runner task. Managed runner UIs observe the same machine request and cannot initiate downloads or installation.

## Runtime boundary

The priority scheduler, private-server Lobby reset, shared team/navigation/match modules, placement playback, and terminal outcome loop are Prototype and preserve the same layer direction. Personalized adaptive-anchor execution remains Planned. New runtime work must extend these cohesive owners rather than turn Debug code or WPF pages into a monolithic runner. See [Project status](PROJECT-STATUS.md) and [Macro architecture](MACRO-ARCHITECTURE.md).
