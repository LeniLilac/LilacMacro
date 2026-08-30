# Deep Debug diagnostics

**Status: Implemented.** Deep Debug Logs default on and are shared by the main Macro shell, Dataset Builder, and Runtime Lab. A dedicated viewer is available for owner inspection.

## Enable, storage, and location

- Main Macro: Settings, Diagnostics, `Enable Deep Debug Logs`.
- Dataset Builder and Runtime Lab: click the title-bar `DEEP DEBUG` pill.
- Capture is fixed at one full-client sample per second. It is diagnostic evidence only and never authorizes input.
- Deep Debug retains the complete one-second frame stream while it fits below 3 GiB. A background worker keeps the newest ten seconds as PNG while they can still become pre-error evidence, then converts older ordinary frames to decode-verified quality-14 JPEG through the in-process Windows imaging component. Once important transition or error-window evidence is older than that same classification window, the worker converts it to pixel-exact AVIF only when the result is smaller; otherwise it remains PNG. A frame whose importance changes during a lossy encode keeps its PNG and is retried losslessly. Only the lossless AVIF path uses the serialized below-normal-priority machine encoder. Any failed encode, decode, dimension check, or lossless pixel check preserves the PNG. Stop drains queued compression for at most ten seconds, then preserves remaining PNGs and continues archive construction. If compression is insufficient, it removes only enough evidence to fit: oldest lossy ordinary frames first, then transition frames, then redundant or lower-severity classified-error windows. A classified error protects frames from ten seconds before through ten seconds after it, and overlapping windows merge.
- Every full-client frame carries privacy-safe capture diagnostics: source pixel format, DXGI color-space observation, measured or fallback SDR white level, tone-map reference scale, bounded Auto-HDR exposure-probe outcome/source/candidate clipping/correlation/failure type, and compact mean/P95/near-white/clipped/dark pixel statistics. These values distinguish an HDR/Auto-HDR capture conversion problem from an already-overexposed Roblox render without storing monitor identifiers, ICC profile names, or additional screenshots.
- After local-instance provisioning, the owner and every configured runner write to protected `%ProgramData%\LilacMacro\Diagnostics`. Without provisioning, the current profile uses `%LOCALAPPDATA%\LilacMacro\diagnostics`.
- `MAX LOG STORAGE, GB` is one shared owner-and-runner archive budget. Its new-machine default is 3 GiB when free space is at most 50 GiB, 10 GiB above 50 GiB, and 30 GiB above 200 GiB. A configured value is lowered when completed logs plus remaining free space cannot support it.
- When free space falls below 3 GiB, new Deep Debug sessions pause temporarily without changing the saved enabled choice. Capture resumes after space is available.
- Existing archives in a former profile-local diagnostics directory are not migrated or deleted automatically.
- `OPEN DEEP DEBUG FOLDER` opens the active archive pool.
- Open `LilacMacro.DeepDebugViewer.exe`, choose `OPEN ARCHIVE`, or drop a ZIP onto the viewer. It does not initialize OCR or Roblox.

Automatic error uploads are a separate privacy choice and default on for a newly presented notice. They use only a completed Deep Debug archive containing a classified error. No light-report fallback exists: when Deep Debug is off or paused for low disk space, automatic error uploads are also paused. Successfully sent and failed archives remain in the shared Deep Debug folder until the configured storage budget removes the oldest archives. There is no manual diagnostic-upload surface.

Expected Plan authoring omissions are recorded as `macro/preflight_blocked` and shown locally with the exact task and corrective setup action. They are not classified errors and do not trigger automatic upload. Unexpected preflight storage, permission, or infrastructure faults remain terminal runtime errors and keep the normal classified-error path.

## Classified evidence

The retained-frame policy recognizes:

- terminal macro/runtime errors and unhandled application exceptions;
- recoverable failures that trigger unattended restart or rejoin;
- bounded UI-state, input, window, docking, or capture failures after retries are exhausted;
- periodic capture gaps while Roblox is intentionally closed, restarting, or temporarily unavailable remain in the event timeline but do not create classified error windows; one repeated gap records its first failure, a bounded periodic summary, and a recovery or stop summary rather than one stack trace per second;
- OCR inference/setup failures that prevent progress;
- failed Setup, Runtime Lab, route-optimizer, or team-swap trials;
- local-session provisioning, launch, or communication failures.

Each error receives a deterministic signature from its workflow/state, failure code, action, and sanitized coarse error identity. When evidence must be reduced, retention prioritizes terminal failures, the first occurrence of each signature, visually distinct occurrences using a coarse perceptual hash, recent occurrences, and repeated near-identical failures in that order. Frames farthest from an error are removed first within the lowest-priority window, allowing the archive to remain close to its capacity instead of discarding a whole useful window for a small overage.

## Archive contract

Each completed operation produces `deep-debug-<operation>-<time>-<id>.zip` containing:

| Entry | Purpose |
|---|---|
| `manifest.json` | Outcome, runtime, evidence counters, hard limits, failures, and privacy policy |
| `events.jsonl` | Ordered machine-readable event stream |
| `timeline.md` | Chronological event index; links can name frames intentionally removed by evidence retention |
| `README.md` | Archive reading order and coordinate convention |
| `configuration/` | Sanitized operation context, Deep Debug options, and environment |
| `frames/` | Complete one-second samples below archive pressure; near the limit, the highest-value samples that fit |
| `frames/index.json` | Per-frame format, encoding mode/quality, original and retained sizes, validation, and importance |
| `visual-profiles/` | Bounded immutable profile revisions and locators consulted by the run |
| `latest-crash-sanitized.txt` | Bounded sanitized tail of a crash log written during this session; earlier resolved crashes are excluded |

Events include window discovery and observed client size, resize results, capture ownership, OCR device/model/cache/timing, OCR and state evaluations, visual-profile scores and coordinates, requested Windows input, cancellation, exceptions, and terminal outcome. Failed input records its initial and final observable Roblox client size, process, elapsed time, bounded action data, and failure type. Main Macro dashboard lines are recorded as timestamped `macro/log` events even after the dashboard's newest-1,000-line display window rolls over.

Events and the timeline normally cover the complete operation. Explicit 128 MiB event and 64 MiB timeline safety bounds prevent an abnormal producer from breaking the single-archive limit; truncation is recorded in the manifest and stream. Visual-profile snapshots remain usage-scoped: at most 64 referenced revisions, 32 files and 8 MiB per revision, and 32 MiB total.

Every ZIP is verified after creation to remain at or below the 3 GiB upload and local-archive hard limit. PNG, JPEG, and AVIF entries are stored without redundant ZIP recompression. `frames/index.json` records each retained frame's format, mode, quality, original/retained size, and validation result; the manifest also records background/final-drain encoding counts, elapsed drain time, and whether the ten-second drain deadline was reached. Finalization runs outside the WPF dispatcher and updates converted frame references with one streaming pass over each log, so a long run does not freeze the interface while repeatedly rescanning the same files. One session completion gate owns writer shutdown, bounded compression drain, indexing, and staging cleanup; concurrent crash and ordinary completion callers await that same result instead of finalizing or deleting one staging directory twice. Below archive pressure, all frames are retained. If compression cannot keep the evidence within 3 GiB, the recorder evicts the lowest-priority frames only until it fits again. If structured data or ZIP overhead makes the completed archive too large, finalization removes approximately the measured excess and rebuilds until the ZIP fits. A finalization failure never changes the primary automation result; the staging directory is preserved with `finalization-error.txt` when possible.

## Agent workflow

1. Copy the relevant ZIP to a privacy-safe local work area; do not commit it.
2. Read `manifest.json`.
3. Read `timeline.md`, then query `events.jsonl` around the failing timestamp.
4. Render bounded visual evidence when needed:

```powershell
./scripts/New-DeepDebugContactSheet.ps1 "path\to\deep-debug.zip" -MaximumFrames 24
```

5. Correlate retained frames with surrounding OCR, vision, window, and input events. Coordinates are Roblox client-relative half-open rectangles.

The viewer streams PNG/JPEG entries and bounded-decodes AVIF entries from the ZIP without persistently extracting them. It offers timestamp-aware playback, nearby events, and optional numbered click/scroll overlays. Missing frames and malformed JSONL records remain explicit failures rather than authorizing or hiding an action. The contact-sheet generator supports all three formats, defaults to ignored output under `artifacts\diagnostic-contact-sheets`, and never changes the source archive.

## Privacy boundary

Private-server links, Discord webhooks, Windows usernames, and profile paths are redacted from text artifacts. Roblox pixels can still expose account, chat, inventory, or other personal game data. Treat every archive as private capture data and never commit it. See [Privacy](../PRIVACY.md#automatic-diagnostic-uploads) for retention and transfer ownership.
