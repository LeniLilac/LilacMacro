# Development

**Status: Current.** This document covers local repository work, not a packaged release.

## Prerequisites

- Windows 10 version 1903 or later, or Windows 11
- The .NET SDK pinned by [`global.json`](../global.json)
- PowerShell 7 for exact CI parity; repository scripts also support Windows PowerShell 5.1
- Optional Python 3.12 and NVIDIA driver/CUDA support for OCR work

## Repository layout

```text
src/LilacMacro.Core       Platform-independent models and deterministic policies
src/LilacMacro.Windows    Win32, Windows Graphics Capture, hotkeys, and input
src/LilacMacro.App        WPF shell, developer workbench, lifecycle, and coordination
tests/LilacMacro.Tests    Deterministic unit and persistence tests
tools/LilacMacro.DatasetTool  Dataset validation and bounded agent views
scripts                   Setup and repository validation commands
schemas                   Normative JSON schemas
docs                      Canonical owner and engineering documentation
eng                       Machine-readable repository policy
```

Dependency direction is:

```text
LilacMacro.Core <- LilacMacro.Windows <- LilacMacro.App
        ^                    ^                 ^
        +------------ tests and tools --------+
```

Core must not reference WPF, Win32, Direct3D, or user-specific paths. Windows owns platform integration but not workflow policy. App composes services and owns UI lifecycle.

Core's `Vision` namespace owns grayscale adaptive-anchor construction, matching, state composition, and profile persistence. Image decoding and dataset selection remain higher-layer concerns; never add per-element detector classes to Core.

## Local setup

```powershell
dotnet restore LilacMacro.slnx --locked-mode
dotnet build LilacMacro.slnx -c Release --no-restore -warnaserror
dotnet test LilacMacro.slnx -c Release --no-build
```

Run the current macro-shell prototype with:

```powershell
dotnet run --project src/LilacMacro.App/LilacMacro.App.csproj
```

Run Dataset Builder, which contains Capture, Review + OCR, and Datasets, with:

```powershell
dotnet run --project src/LilacMacro.App/LilacMacro.App.csproj -- --dataset-builder
```

Run Runtime Lab, which contains Debug and Wire Test, with:

```powershell
dotnet run --project src/LilacMacro.App/LilacMacro.App.csproj -- --runtime-lab
```

Every normal build also creates `LilacMacro.DatasetBuilder.exe` and `LilacMacro.RuntimeLab.exe` beside `LilacMacro.exe`. The dedicated launchers open their focused shells directly when double-clicked. Dataset Builder is a GUI and must not be confused with the headless `LilacMacro.DatasetTool` validation utility.

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

## Persistence and local state

| Owner | Default location | Write behavior |
|---|---|---|
| Dataset Builder | `Documents\LilacMacro Datasets` | Draft-first; manifest and image writes use temporary files; finalization never overwrites |
| App capture settings | `%LOCALAPPDATA%\LilacMacro\settings.json` | Atomic replacement |
| Placement authoring | `%LOCALAPPDATA%\LilacMacro\placements` | Validated snapshots, serialized save queue, atomic replacement |
| OCR runtime | `%LOCALAPPDATA%\LilacMacro\ocr` | Isolated Python environment and device marker |
| Crash logging | `%LOCALAPPDATA%\LilacMacro\logs\latest-crash.txt` | Latest unhandled WPF exception |
| Deep debug | `%LOCALAPPDATA%\LilacMacro\diagnostics` | Bounded ZIP archives plus transient staging; settings use atomic replacement |

Never commit any of this state. See [Privacy](../PRIVACY.md).

## UI conventions

[DESIGN.md](../DESIGN.md) is authoritative. Use centralized semantic theme resources, centralized Lucide vector geometry, and the implicit thin scrollbar style. Every semantic brush key must exist in both `ThemeColors` dictionaries and UI consumers must use `DynamicResource`; repository policy rejects static theme-brush references. Preserve the browser-style top tabs and terse owner-tool wording. The owner performs live visual testing; agents must not operate LilacMacro or Roblox through computer-control tooling.

The dock target is expressed in physical client pixels. Convert its WPF size through the current monitor DPI before positioning Roblox, keep the full target inside the visible owner window, and restore Roblox before the Macro page unloads or the application closes. Do not redock merely because Roblox becomes foreground while LilacMacro is already behind another application; a docked source may retain focus only when focus moved directly from the active owner. Normal startup bounds fit the monitor work area, and maximized bounds must use `WM_GETMINMAXINFO` work-area dimensions so the taskbar remains visible.

## Source limits

`scripts/Test-RepositoryPolicy.ps1` enforces:

- 500 lines for production C#, XAML, Python, and repository PowerShell;
- 800 lines for tests;
- 120 lines for every `AGENTS.md`.

Split by cohesive ownership before a file crosses its limit. Existing debt, if introduced deliberately, must use an exact ceiling in [`eng/repository-policy.json`](../eng/repository-policy.json) and shrink whenever the file shrinks.

## Documentation changes

Add new canonical documents to [the documentation index](README.md), route them from root `AGENTS.md` when agents must read them, and keep links relative. Run `scripts/Test-Documentation.ps1`; it rejects unresolved local links, missing headings, omitted canonical docs, and personal absolute paths.
