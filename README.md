# LilacMacro

LilacMacro is a Windows desktop tool for building reliable visual datasets before automating Roblox workflows. It keeps the first workflow deliberately structured:

1. Find Roblox and verify an exact client resolution.
2. Capture fresh frames either on a timer or manually one frame at a time.
3. Name and review the dataset with per-frame verdicts, zoomable boxes, labels, notes, and PP-OCRv6 detection + recognition trials.

The app does not inject into Roblox or read its process memory. It uses Win32 window management and Windows Graphics Capture.

## Requirements

- Windows 10 version 1903 or later, or Windows 11
- .NET 10 SDK
- Roblox running in windowed mode

## Build

```powershell
dotnet restore LilacMacro.slnx
dotnet build LilacMacro.slnx -c Release --no-restore
dotnet test LilacMacro.slnx -c Release --no-build
```

Run the app with:

```powershell
dotnet run --project src/LilacMacro.App/LilacMacro.App.csproj
```

Datasets default to `Documents\LilacMacro Datasets`, outside the repository. Do not commit personal or third-party captures.

F6 is registered globally while LilacMacro is running. It starts a capture with the saved settings without changing the visible page; presses during an active capture are ignored. Each completed press creates a separate draft dataset.

Manual mode creates an open draft without a timer. While that session is active, F5 is registered globally and appends one fresh frame per press. Finish the session from Capture before changing its resolution or dataset root.

## Optional OCR setup

Region OCR runs in an isolated Python 3.12 environment under `%LOCALAPPDATA%\LilacMacro\ocr`; it does not alter the global Python environment. Set it up with:

```powershell
.\scripts\Setup-Ocr.ps1 -Device cpu
# or, for a supported NVIDIA GPU:
.\scripts\Setup-Ocr.ps1 -Device gpu
```

The review workspace pairs `PP-OCRv6_small_rec` with `PP-OCRv6_small_det` and `PP-OCRv6_tiny_rec` with `PP-OCRv6_tiny_det`. Detection runs only inside the manually selected region and stores each recognized line with original-frame coordinates. Select `CPU` or `GPU` beside the model; a trial records the exact device used. Model files download on first use and stay in the local Paddle model cache, never in this repository. `KEEP LOADED` caches pipelines by model and device in the current app session; turning it off releases the worker.

Mouse wheel zooms the active annotation or OCR map around the pointer. Hold the middle mouse button and drag to pan. The toolbar buttons and `FIT` work on either view. `MAP ONLY` expands the clean OCR text map to the full review canvas.

## Dataset layout

Each dataset directory contains `dataset.json` and an `images` directory. Capture sessions begin in a `.draft-*` directory and are finalized only after review gives them a name. See [docs/DATASET-FORMAT.md](docs/DATASET-FORMAT.md).

Datasets are self-describing against [schemas/dataset.schema.json](schemas/dataset.schema.json). Agents should use the validated contact-sheet and JSONL workflow in [docs/AGENT-DATASET-WORKFLOW.md](docs/AGENT-DATASET-WORKFLOW.md), rather than opening an unbounded image directory frame by frame.
