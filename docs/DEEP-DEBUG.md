# Deep Debug diagnostics

**Status: Implemented.** Deep Debug Logs default on and are shared by the main Macro shell, Dataset Builder, and Runtime Lab. A dedicated viewer is available for owner inspection.

## Enable, storage, and location

- Main Macro: Settings, Diagnostics, `Enable Deep Debug Logs`.
- Dataset Builder and Runtime Lab: click the title-bar `DEEP DEBUG` pill.
- Capture is fixed at one full-client sample per second. It is diagnostic evidence only and never authorizes input.
- Deep Debug keeps a rolling ten-second pre-error buffer. A classified error preserves frames from ten seconds before through ten seconds after it. Overlapping windows merge. Important state transitions retain one representative frame even when no error occurs.
- After local-instance provisioning, the owner and every configured runner write to protected `%ProgramData%\LilacMacro\Diagnostics`. Without provisioning, the current profile uses `%LOCALAPPDATA%\LilacMacro\diagnostics`.
- `MAX LOG STORAGE, GB` is one shared owner-and-runner archive budget. Its new-machine default is 3 GiB when free space is at most 50 GiB, 10 GiB above 50 GiB, and 30 GiB above 200 GiB. A configured value is lowered when completed logs plus remaining free space cannot support it.
- When free space falls below 3 GiB, new Deep Debug sessions pause temporarily without changing the saved enabled choice. Capture resumes after space is available.
- Existing archives in a former profile-local diagnostics directory are not migrated or deleted automatically.
- `OPEN DEEP DEBUG FOLDER` opens the active archive pool.
- Open `LilacMacro.DeepDebugViewer.exe`, choose `OPEN ARCHIVE`, or drop a ZIP onto the viewer. It does not initialize OCR or Roblox.

Automatic error uploads are a separate privacy choice and default on for a newly presented notice. They use only a completed Deep Debug archive containing a classified error. No light-report fallback exists: when Deep Debug is off or paused for low disk space, automatic error uploads are also paused. A successful upload removes the local archive; a failed upload leaves it in the shared Deep Debug folder. There is no manual diagnostic-upload surface.

## Classified evidence

The retained-frame policy recognizes:

- terminal macro/runtime errors and unhandled application exceptions;
- recoverable failures that trigger unattended restart or rejoin;
- bounded UI-state, input, window, docking, or capture failures after retries are exhausted;
- OCR inference/setup failures that prevent progress;
- failed Setup, Runtime Lab, route-optimizer, or team-swap trials;
- local-session provisioning, launch, or communication failures.

Each error receives a deterministic signature from its workflow/state, failure code, action, and sanitized coarse error identity. When evidence must be reduced, retention prioritizes terminal failures, the first occurrence of each signature, visually distinct occurrences using a coarse perceptual hash, recent occurrences, and repeated near-identical failures in that order. Whole low-priority windows are discarded before higher-value evidence.

## Archive contract

Each completed operation produces `deep-debug-<operation>-<time>-<id>.zip` containing:

| Entry | Purpose |
|---|---|
| `manifest.json` | Outcome, runtime, evidence counters, hard limits, failures, and privacy policy |
| `events.jsonl` | Ordered machine-readable event stream |
| `timeline.md` | Chronological event index; links can name frames intentionally removed by evidence retention |
| `README.md` | Archive reading order and coordinate convention |
| `configuration/` | Sanitized operation context, Deep Debug options, and environment |
| `frames/` | One-second samples selected from classified error windows and important transitions |
| `visual-profiles/` | Bounded immutable profile revisions and locators consulted by the run |
| `latest-crash-sanitized.txt` | Bounded tail of the latest crash log when available |

Events include window discovery and observed client size, resize results, capture ownership, OCR device/model/cache/timing, OCR and state evaluations, visual-profile scores and coordinates, requested Windows input, cancellation, exceptions, and terminal outcome. Main Macro dashboard lines are recorded as timestamped `macro/log` events even after the dashboard's newest-1,000-line display window rolls over.

Events and the timeline normally cover the complete operation. Explicit 128 MiB event and 64 MiB timeline safety bounds prevent an abnormal producer from breaking the single-archive limit; truncation is recorded in the manifest and stream. Visual-profile snapshots remain usage-scoped: at most 64 referenced revisions, 32 files and 8 MiB per revision, and 32 MiB total.

Every ZIP is verified after creation to remain at or below the 3 GiB upload and local-archive hard limit. Frame evidence targets at most 2.5 GiB, leaving headroom for structured events, configuration, visual profiles, manifest, and ZIP overhead. A finalization failure never changes the primary automation result; the staging directory is preserved with `finalization-error.txt` when possible.

## Agent workflow

1. Copy the relevant ZIP to a privacy-safe local work area; do not commit it.
2. Read `manifest.json`.
3. Read `timeline.md`, then query `events.jsonl` around the failing timestamp.
4. Render bounded visual evidence when needed:

```powershell
./scripts/New-DeepDebugContactSheet.ps1 "path\to\deep-debug.zip" -MaximumFrames 24
```

5. Correlate retained frames with surrounding OCR, vision, window, and input events. Coordinates are Roblox client-relative half-open rectangles.

The viewer streams PNG entries from the ZIP without extracting them. It offers timestamp-aware playback, nearby events, and optional numbered click/scroll overlays. Missing frames and malformed JSONL records remain explicit failures rather than authorizing or hiding an action. Contact sheets default to ignored output under `artifacts\diagnostic-contact-sheets` and never change the source archive.

## Privacy boundary

Private-server links, Discord webhooks, Windows usernames, and profile paths are redacted from text artifacts. Roblox pixels can still expose account, chat, inventory, or other personal game data. Treat every archive as private capture data and never commit it. See [Privacy](../PRIVACY.md#automatic-diagnostic-uploads) for retention and transfer ownership.
