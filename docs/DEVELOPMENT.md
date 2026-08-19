# Development

**Status: Current.** This document covers local repository work, not a packaged release.

## Prerequisites

- Windows 10 version 1903 or later, or Windows 11
- The .NET SDK pinned by [`global.json`](../global.json)
- PowerShell 7 for exact CI parity; repository scripts also support Windows PowerShell 5.1
- Python 3.12 is required when building the bundled OCR runtime; NVIDIA driver/CUDA support is optional for GPU OCR work

## Repository layout

```text
src/LilacMacro.Core       Platform-independent models and deterministic policies
src/LilacMacro.Windows    Win32, Windows Graphics Capture, hotkeys, and input
src/LilacMacro.App        WPF shell, developer workbench, lifecycle, and coordination
src/LilacMacro.App/Assets/RuntimeEvidence  Curated release evidence slices
src/LilacMacro.Runtime    WPF-free shared Story/Raid/Challenge scheduler and workflow composition
src/LilacMacro.SessionSetup  Elevated allowlisted local-session provisioning helper
src/LilacMacro.SessionWorker Windowless one-shot runner-profile policy bootstrap retained for setup compatibility
tests/LilacMacro.Tests    Deterministic unit and persistence tests
tools/LilacMacro.DatasetTool  Dataset validation and bounded agent views
scripts                   Setup and repository validation commands
schemas                   Normative JSON schemas
docs                      Canonical owner and engineering documentation
eng                       Machine-readable repository policy
```

Dependency direction is:

```text
LilacMacro.Core <- LilacMacro.Windows <- LilacMacro.Runtime <- LilacMacro.App
        ^                    ^                  ^                  ^
        +---------------- tests and tools consume lower layers ---+
```

Core must not reference WPF, Win32, Direct3D, or user-specific paths. Windows owns platform integration but not workflow policy. Runtime composes reusable WPF-free workflows. App owns UI lifecycle and desktop orchestration.

Core's `Vision` namespace owns grayscale adaptive-anchor construction, matching, state composition, and profile persistence. Image decoding and dataset selection remain higher-layer concerns; never add per-element detector classes to Core.

Local-instance contracts remain in Core, system mutation remains in Windows, and desktop/instance-manager orchestration remains in App. SessionSetup may compose Core and Windows only. SessionWorker performs only the one-shot runner-profile policy pass in the active design; no scheduled task starts its legacy headless runtime. Do not move workflow policy into either executable entrypoint.

## Local setup

```powershell
dotnet restore LilacMacro.slnx --locked-mode
dotnet build LilacMacro.slnx -c Release --no-restore -warnaserror
dotnet test LilacMacro.slnx -c Release --no-build
```

Validate the installer without mutating Windows:

```powershell
./scripts/Test-Installer.ps1
```

Building a local installer additionally requires Inno Setup 6 and `-UnsignedDevelopmentBuild`; that artifact cannot be published. Official project-signed artifacts are produced only by the pinned GitHub Actions release workflow using its protected Ed25519 secret. No Authenticode certificate is used. Do not run the elevated helper on the owner's machine during agent work unless the owner explicitly authorizes it for the current task. See [Installer](INSTALLER.md).

Run the current macro-shell prototype with:

```powershell
dotnet run --project src/LilacMacro.App/LilacMacro.App.csproj
```

Run Dataset Builder, which contains Capture, Review + OCR, and Datasets, with:

```powershell
dotnet run --project src/LilacMacro.App/LilacMacro.App.csproj -- --dataset-builder
```

Run Runtime Lab, which contains Debug, Wire Test, the temporary Scroll Test, Team Swap Test, Route Optimizer Test, and production-backed utility task tests, with:

```powershell
dotnet run --project src/LilacMacro.App/LilacMacro.App.csproj -- --runtime-lab
```

Run the Deep Debug Viewer without initializing OCR or Roblox with:

```powershell
dotnet run --project src/LilacMacro.App/LilacMacro.App.csproj -- --deep-debug-viewer
```

Every normal build and publish also creates `LilacMacro.DatasetBuilder.exe`, `LilacMacro.RuntimeLab.exe`, and `LilacMacro.DeepDebugViewer.exe` beside `LilacMacro.exe`. The dedicated launchers share the stable `LilacMacro.dll` payload and open their focused shells directly when double-clicked. The Deep Debug Viewer constructs only its bounded archive reader. Do not publish the renamed shells concurrently through one intermediate directory; publish the stable assembly once and use these generated launchers. Dataset Builder is a GUI and must not be confused with the headless `LilacMacro.DatasetTool` validation utility.

## Local artifact versions

Runnable local artifacts use one sortable name at the root of `artifacts`:

```text
macro-1.0.0-optional-label
datasetbuilder-1.0.0-optional-label
runtimelab-1.0.0-optional-label
deepdebugviewer-1.0.0-optional-label
```

The numeric part is authoritative. Compare semantic versions before labels: `1.0.2` is newer than `1.0.1` regardless of its label. A label is optional lowercase kebab-case context such as `gpu-warm-team-swap`; it is not a version or a substitute for incrementing the version.

Create a matched artifact set from the same source with:

```powershell
./scripts/Publish-LocalArtifacts.ps1 -Version 1.0.0 -Label gpu-warm-team-swap
```

If `-Version` is omitted, the script uses `VersionPrefix` from [`Directory.Build.props`](../Directory.Build.props). Publishing never overwrites an existing artifact folder. Increment the patch number for every new owner-test build; use a minor number for a compatible milestone and a major number only for an intentionally breaking artifact or persisted-data contract.

Each folder contains exactly one primary executable plus the shared payload and a `BUILD-INFO.txt` recording its type, version, label, source commit, dirty-state flag, and build time. Folders with the same version and label are a matched set. Keep the highest version needed for current testing and any deliberate rollback version; older runnable folders may be deleted. Unversioned runnable folders are legacy local builds and may be deleted after the latest labeled set opens successfully. `diagnostic-contact-sheets` and image previews are generated diagnostics rather than runnable versions and may be removed whenever their evidence is no longer needed.

Nothing under `artifacts` is authoritative application state. Deleting an old artifact does not delete datasets, placements, settings, OCR models, logs, or deep-debug archives; those locations are listed under [Persistence and local state](#persistence-and-local-state).

`MacroShellWindow` remains the default startup window. The tool switches and dedicated launchers route the shared `MainWindow` to one explicit page profile without adding developer navigation to the macro shell.

For OCR environment setup, see [OCR and vision](OCR-AND-VISION.md). For all validation commands, see [Testing](TESTING.md).

## Code conventions

- Nullable reference types and implicit usings are enabled.
- Prefer small domain types, records, explicit validation, and deterministic pure policies.
- Use asynchronous file, capture, OCR, and persistence APIs; do not block the WPF UI thread.
- Keep cancellation tokens through long-running or external operations.
- Fail closed when window identity, size, state evidence, coordinates, or persisted data are invalid.
- Avoid broad helpers, god classes, compatibility facades, and partial classes used only to evade file limits.
- Keep user-visible behavior and status documentation current in the same change.

## Coordinate systems

- The macro runtime and dashboard dock use a canonical `1366 x 700` Roblox client area.
- Requested resolutions are Roblox client-area dimensions.
- Dataset and placement coordinates are original-image pixels.
- Rectangles are half-open: `x` and `y` identify the inclusive origin; width and height identify the covered extent.
- Convert a verified client-relative point to desktop coordinates only immediately before Windows input.
- Never persist desktop coordinates or outer-window bounds as gameplay positions.
- Detector capture regions use the same client-relative half-open coordinates. Their combined requested area is bounded to one client frame and their atlas coordinates are transient implementation details.

Every semantic detector search area must be owned by a named bundled dataset annotation. Static rectangles are centralized in `RuntimeSearchRegionEvidenceCatalog`; dynamic rectangles derive from freshly verified dataset-owned bounds. See [Runtime evidence ownership](RUNTIME-EVIDENCE.md).

## Persistence and local state

| Owner | Default location | Write behavior |
|---|---|---|
| Dataset Builder | `Documents\LilacMacro Datasets` | Draft-first; manifest and image writes use temporary files; finalization never overwrites |
| App capture settings | `%LOCALAPPDATA%\LilacMacro\settings.json` | Atomic replacement |
| Macro settings | `%LOCALAPPDATA%\LilacMacro\macro-settings.json` before local-instance setup; `%ProgramData%\LilacMacro\Configurations\shared` afterward | Schema-versioned, cross-process-serialized atomic replacement for display/update preferences, keybinds, Plan snapshots/selection/reporting options, and DPAPI ciphertext for private-server/webhook values; `privacy-choices.json` independently owns the authoritative monotonic consent generation so stale ordinary saves cannot re-enable a choice; the shared ProgramData root uses machine-scope DPAPI plus an owner/runner ACL |
| Macro runtime progress | Active Macro configuration root under `macro-runtime-progress-<instance>.json` | Atomic, cross-process-serialized saves for per-plan victories, defeats, loop runs, and utility due times; elapsed timer and run chart remain start-scoped; the Plan page's RESET PROGRESS action clears the saved snapshot |
| Placement authoring | Active configuration root under `placements` | Validated snapshots, serialized save queue, atomic replacement; shared and isolated runner modes select different roots |
| Local instance profiles | `%ProgramData%\LilacMacro\Profiles` | Owner-journaled runner identity/policy receipts; one ACL-restricted directory per runner |
| OCR runtime | Installed `ocr\python` plus `%LOCALAPPDATA%\LilacMacro\ocr` | Read-only bundled CPU runtime; per-user GPU environment, model cache, and device markers |
| Crash logging | `%LOCALAPPDATA%\LilacMacro\logs\latest-crash.txt` | Latest unhandled WPF exception |
| Deep debug | `%LOCALAPPDATA%\LilacMacro\diagnostics` | Bounded ZIP archives plus transient staging; settings use atomic replacement |
| Application updates | `%LOCALAPPDATA%\LilacMacro\updates`; coordinated request under `%ProgramData%\LilacMacro\Updates` | Public metadata and owner-approved downloads stay per owner; the machine request contains only exact version/hash/process/session/profile coordination data and is cleared after install/relaunch |

Never commit any of this state. See [Privacy](../PRIVACY.md).

## UI conventions

[DESIGN.md](../DESIGN.md) is authoritative. Use centralized semantic theme resources, centralized Lucide vector geometry, and the implicit thin scrollbar style. Every semantic brush key must exist in both `ThemeColors` dictionaries and UI consumers must use `DynamicResource`; repository policy rejects static theme-brush references. Selectable palette overlays may replace visual-surface resources at runtime, but status, plan-state, match-step, capture, annotation, and OCR evidence colors stay owned by the base Light/Dark dictionaries. Gradient themes may affect the canvas and primary accent fills, never text or evidence. Preserve the browser-style top tabs and terse owner-tool wording. The owner performs live visual testing; agents must not operate LilacMacro or Roblox through computer-control tooling.

The dock target is expressed in physical client pixels. Convert its WPF size through the current monitor DPI before positioning Roblox, keep the full target inside the visible owner window, and restore Roblox before the Macro page unloads or the application closes. Undock and automatic release paths restore only the standalone styles at the last docked position and size, so the owner can reposition or resize Roblox after release. Use the shared exposure rule for both acquisition and maintenance: an overlapping unrelated foreground window suspends docking, while a verified foreground Roblox source may redock once and must remain docked on the next maintenance tick. Normal startup bounds fit the monitor work area, and maximized bounds must use `WM_GETMINMAXINFO` work-area dimensions so the taskbar remains visible.

## Source limits

`scripts/Test-RepositoryPolicy.ps1` enforces:

- 500 lines for production C#, XAML, Python, and repository PowerShell;
- 800 lines for tests;
- 120 lines for every `AGENTS.md`.

Split by cohesive ownership before a file crosses its limit. Existing debt, if introduced deliberately, must use an exact ceiling in [`eng/repository-policy.json`](../eng/repository-policy.json) and shrink whenever the file shrinks.

## Documentation changes

Add new canonical documents to [the documentation index](README.md), route them from root `AGENTS.md` when agents must read them, and keep links relative. Run `scripts/Test-Documentation.ps1`; it rejects unresolved local links, missing headings, omitted canonical docs, and personal absolute paths.
