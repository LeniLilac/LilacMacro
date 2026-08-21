# Deep debug diagnostics

**Status: Implemented.** Deep Debug Logs default on and are shared by the main Macro shell, Dataset Builder, and Runtime Lab. A dedicated viewer is also implemented for owner inspection.

## Enable and locate

- Main Macro: Settings, Diagnostics, `Deep Debug Logs`.
- Dataset Builder: click the `DEEP DEBUG` pill in the title bar.
- Runtime Lab: click the `DEEP DEBUG` pill in the title bar.
- Set `FRAME HISTORY, MIN` from 1 to 120. New installs default to 30 minutes; the value and enabled state persist locally.
- Set `CAPTURE INTERVAL` to 0.5, 1.0, 2.0, or 5.0 seconds. New installs default to five seconds. While a session is active, its registered Roblox surface captures one full client frame at that interval; the interval is persisted independently from frame history.
- Runtime Lab retains already-acquired PNG evidence for its complete operation and displays `DEEP DEBUG ON`; the frame-history setting applies to the main Macro and Dataset Builder only.
- Archives are written to `%LOCALAPPDATA%\LilacMacro\diagnostics`.
- Settings exposes `OPEN DEBUG FOLDER`.
- `NEWEST DEEP DEBUG LOGS` always limits local retention to the configured newest completed archives, from 1 to 100 and defaulting to 10. The same limit applies whether automatic reports are enabled or disabled.
- Automatic reports default off. When enabled, a completed active Deep Debug archive is queued automatically and deleted only after the service accepts it. A failed transfer leaves the archive local. There is no manual diagnostic-upload surface.
- Open `LilacMacro.DeepDebugViewer.exe`, choose `OPEN ARCHIVE`, or drop a ZIP onto the window. The viewer does not initialize OCR or Roblox.

The main Macro records its scheduler lifecycle, state and input evidence, terminal result, and private-server reset status without recording the private link. Dataset Builder records timed capture, an entire manual-capture session, and standalone OCR trials. Runtime Lab records Debug actions, complete Wire Tests, randomized Team Swap Test batches, and Route Optimizer batches with their selected resource, threshold, compact OCR text, decision, elapsed time, click, and transition evidence.

## Archive contract

Each completed operation produces `deep-debug-<operation>-<time>-<id>.zip` containing:

| Entry | Purpose |
|---|---|
| `manifest.json` | Outcome, runtime, counters, retention, failures, and privacy policy |
| `events.jsonl` | Complete ordered machine-readable event stream |
| `timeline.md` | Fast chronological index with links to retained evidence |
| `README.md` | Archive reading order and coordinate convention |
| `configuration/` | Sanitized operation context, runtime options, and environment |
| `frames/` | Retained PNG captures and detector regions: rolling window for Main Macro/Dataset Builder, complete operation for Runtime Lab |
| `visual-profiles/` | Bounded immutable profile revisions and locators actually consulted by the run |
| `latest-crash-sanitized.txt` | Latest crash log when one exists and can be read |

Events include window discovery and observed client size, resize results, capture ownership, dataset frame identity, OCR model/device/cache and timing fields, OCR text and boxes, state evaluations, visual-profile scores and coordinates, requested Windows input, cancellation, exceptions, and terminal outcome.

While a Main Macro session is active, every dashboard trace-log line is also recorded as a `macro/log` event with the exact timestamped line produced for the UI. The dashboard displays only the newest 1,000 lines and refreshes that bounded window in batches so long runs remain responsive; the archive still preserves every recorded `macro/log` event. This preserves intermediate progress and failure details—such as a missing physical selection proof—alongside the later generic recovery outcome.

An OCR-owned visual-profile refresh records `vision/profile_refresh_failed` with the exception type and sanitized message when persistence or comparison fails. A registered profile whose locator is unavailable marks the archive manifest's writer failure instead of silently omitting the locator from the snapshot.

Visual-profile snapshots are usage-scoped rather than a copy of the user's profile library. A session retains at most 64 referenced revisions, 32 files and 8 MiB per revision, and 32 MiB total. JSON paths are redacted; PGM assets remain exact so another machine can reproduce the recorded matcher input.

PNG evidence includes pixels already acquired by the operation plus intentional periodic full-client samples for the configured capture interval. Periodic samples are recorded as `frame/live-client` with `CaptureReason: deep-debug-interval`; they are not used to authorize input. Detector evidence contains only the compact requested region; live OCR and full-state evidence contains the already-required Roblox client or crop.

## Retention and failure behavior

- `events.jsonl` and `timeline.md` cover the complete operation.
- `CAPTURE INTERVAL` controls sampling frequency; `FRAME HISTORY, MIN` controls how long PNG evidence remains in the rolling archive. Frame history is retention time, not a second capture scheduler.
- Main Macro and Dataset Builder remove PNGs older than the configured rolling frame window before archival. Runtime Lab retains PNG evidence for the complete operation.
- The configured newest-log count is enforced at startup, whenever the setting changes, and after a new archive succeeds. It applies equally to automatically reported and local-only Deep Debug sessions.
- One session owns the recorder at a time.
- A diagnostics writer or ZIP failure never changes the primary operation result. The staging directory is preserved with `finalization-error.txt` when possible.
- App shutdown and unhandled WPF exceptions finalize an active session.

## Agent workflow

1. Copy the relevant ZIP to a privacy-safe local work area; do not commit it.
2. Read `manifest.json`.
3. Read `timeline.md`, then query `events.jsonl` for the failing state or timestamp.
4. Render bounded visual evidence when needed:

```powershell
./scripts/New-DeepDebugContactSheet.ps1 "path\to\deep-debug.zip" -MaximumFrames 24
```

5. Correlate a frame event with the immediately surrounding OCR, vision, window, and input events. Coordinates are Roblox client-relative half-open rectangles.

Contact sheets draw magenta click crosshairs and yellow drag paths on the last selected full-client frame preceding each input. The label preserves the exact client-relative coordinates used by the macro.

The desktop Deep Debug Viewer streams PNG entries directly from the ZIP without extracting them. It provides timestamp-aware playback, nearby machine events, and optional numbered click/scroll overlays translated from client-relative coordinates into full-frame or cropped-frame space. Missing frames and malformed JSONL records remain visible as explicit failures rather than authorizing or hiding an action.

The contact sheet defaults to ignored output under `artifacts\diagnostic-contact-sheets` and never changes the source archive.

## Privacy boundary

Private-server links, Discord webhooks, Windows usernames, and Windows profile paths are redacted from text artifacts. Roblox pixels can still expose account, chat, inventory, or other personal game data. Treat every archive as private capture data and never commit it. Automatic reporting is a separate default-off choice; there is no manual transfer surface. Successfully sent automatic archives are deleted; a bounded exact-name marker retries deletion at orderly close and startup if Windows temporarily locks the ZIP. See [Privacy](../PRIVACY.md#automatic-diagnostic-uploads) for limits, retention, and transfer ownership.
