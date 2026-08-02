# Architecture

LilacMacro starts with the evidence-building portion of a macro rather than macro playback.

```text
LilacMacro.Core <- LilacMacro.Windows <- LilacMacro.App
        ^                                  |
        +---------- LilacMacro.Tests ------+
```

## Core

Core owns immutable pixel geometry, capture-plan validation, deterministic sample timing, dataset manifests, safe dataset names, and atomic JSON persistence. It has no WPF, Win32, or Direct3D references.

## Windows

Windows enumerates visible top-level windows, admits only Roblox-owned candidates, measures client-relative geometry, and resizes by correcting the observed difference between outer and client bounds. A resize succeeds only after the requested client dimensions are observed twice.

Windows Graphics Capture owns fresh-frame acquisition. Frames are captured as 16-bit floating-point scRGB, cropped to the measured client area, tone-mapped to sRGB, and encoded as PNG. This keeps captures useful on SDR and HDR desktops and avoids screen-coordinate crops that include window chrome.

## App

The WPF app composes three workspaces:

- **Capture:** sizing and timed/manual sampling controls with live verification, global F5 single-frame capture, and global F6 timed capture.
- **Review:** image strip, frame verdict, zoomable annotation canvas, annotation fields, dataset notes, and side-by-side or map-only OCR visualization.
- **Datasets:** finalized and recoverable draft sessions discovered under the chosen root.

The app persists only non-secret preferences under `%LOCALAPPDATA%\LilacMacro`. Dataset images and annotations stay under the owner-selected dataset root.

## OCR boundary

OCR is an optional local helper process. A repository script creates a Python 3.12 virtual environment under `%LOCALAPPDATA%\LilacMacro\ocr` with pinned PaddleOCR and either the official CPU or CUDA 12.6 PaddlePaddle package. The WPF app writes only the selected image crop to a temporary PNG, runs an allowlisted detector/recognizer pair on the selected `cpu` or `gpu:0` device, shifts every detected child rectangle into original-frame coordinates, reads a bounded JSON result, and deletes the crop. By default the process exits after a run. `KEEP LOADED` owns one child process and exchanges atomic request/response JSON files through a unique temporary channel with a hard deadline, worker-exit detection, and cleanup; pipelines are cached by model and device until toggled off or the app exits. OCR output, device, timings, and confidence are stored as annotation trials; they never authorize future input without independent visual ownership.
