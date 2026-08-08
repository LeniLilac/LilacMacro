# Troubleshooting

**Status: Current local-development and prototype guidance.** There is no packaged installer or supported release artifact yet.

## SDK or restore fails

Check the pinned SDK:

```powershell
dotnet --info
Get-Content global.json
dotnet restore LilacMacro.slnx --locked-mode
```

Install the requested .NET 10 SDK if it is missing. Do not regenerate package lock files merely to bypass a restore mismatch; determine whether the central package definition intentionally changed.

## App will not start

Build with warnings treated as errors:

```powershell
dotnet build LilacMacro.slnx -c Release --no-restore -warnaserror
```

The latest unhandled WPF exception is written to `%LOCALAPPDATA%\LilacMacro\logs\latest-crash.txt`. It may contain local paths; do not commit or publish it. `LilacMacro.exe` opens the macro shell, `LilacMacro.DatasetBuilder.exe` opens Capture/Review/Datasets, and `LilacMacro.RuntimeLab.exe` opens Debug/Wire Test.

## Deep debug archive is missing

- Enable `Deep debug trace` under Macro Settings, Diagnostics, or click the Dataset Builder/Runtime Lab title-bar `DEEP DEBUG` pill.
- Complete or stop the operation. The ZIP is finalized at operation end, cancellation, shutdown, or an unhandled WPF exception.
- Open `%LOCALAPPDATA%\LilacMacro\diagnostics` from Settings with `OPEN DEBUG FOLDER`.
- A `.deep-debug-*` directory with `finalization-error.txt` means ZIP creation failed and the uncompressed evidence was preserved.
- Only 20 completed archives are retained. Copy an important archive elsewhere before extended testing.

Read [Deep debug](DEEP-DEBUG.md) before sharing or inspecting an archive. To render bounded frames:

```powershell
./scripts/New-DeepDebugContactSheet.ps1 "path\to\deep-debug.zip"
```

## OCR is not ready

Install Python 3.12 and ensure the Windows `py` launcher can resolve it:

```powershell
py -3.12 -c "import sys; print(sys.version)"
./scripts/Setup-Ocr.ps1 -Device cpu
```

OCR lives under `%LOCALAPPDATA%\LilacMacro\ocr`; it does not use the global Python environment after setup.

## GPU setup fails

- Confirm `nvidia-smi` is available and the driver supports the CUDA 12.6 Paddle package.
- Run `./scripts/Setup-Ocr.ps1 -Device gpu` and read the final import/device check.
- Setup installs either CPU or GPU PaddlePaddle, not both. Rerun CPU setup to return to the CPU runtime.
- A UI device toggle selects a requested runtime; it cannot make an uninstalled runtime available.

## KEEP LOADED hangs or a worker stops

Turn `KEEP LOADED` off to terminate the resident helper, then retry one-shot OCR. If needed, close LilacMacro to release the process and temporary channel. Re-run OCR setup only if the device readiness check fails. Keep the exact error text, selected device, model, load time, inference time, and crop dimensions when reporting the issue.

## Roblox size is wrong

- Use windowed Roblox and make sure its client is visible.
- Requested dimensions are client-area pixels, not the outer window.
- Refresh discovery, then apply the target again. Resize succeeds only after the requested live client size is observed twice.
- Runtime Lab requires exactly `1366 x 700`; Dataset Builder defaults to `1280 x 720` but accepts explicit target dimensions.
- A display, monitor, DPI, or window-state change invalidates old geometry; reapply and reobserve before capture or input.

## Roblox will not dock

- Open one supported Roblox player window, return to Macro, and press `Dock`. LilacMacro maximizes when the current viewport cannot contain the physical `1366 x 700` client.
- `Roblox Not Found` means no verified visible player window was discovered. `View Too Small` means the complete dock target is not visible inside the LilacMacro window.
- The dock pauses and restores the standalone frame when another window covers the dashboard. Return Macro to the foreground to dock again.
- Press `Undock` before moving Roblox independently. Tab changes and normal shutdown also restore the original frame and bounds.
- If restoration fails, leave LilacMacro open and keep the error toast. Do not terminate the app until Roblox is standalone; if necessary, restart Roblox after closing other software that embeds or restyles it.

## Capture fails or stops

- Finish any active manual session before changing size/root or opening Runtime Lab.
- Keep Roblox unobscured enough for Windows Graphics Capture to deliver frames and do not resize it during a session.
- A surface-size change intentionally stops capture rather than saving mismatched images.
- Draft directories beginning with `.draft-` are recoverable and visible to the dataset workbench.

## F5 or F6 does nothing

- F5 is registered only while a manual capture session is active; each accepted press appends one fresh frame.
- F6 normally starts a separate timed draft with saved settings. Presses during active capture are ignored.
- Dataset Builder owns F5 for active manual capture and F6 for timed drafts.
- Runtime Lab owns F6 only while a Debug key chain is armed for start/cancel.
- Another application may already own the global hotkey. Close the conflict and restart the relevant tool.
- The macro shell does not expose either owner-tool hotkey surface.

## Dataset will not open or finalize

Validate it directly:

```powershell
dotnet run --project tools/LilacMacro.DatasetTool -- validate "C:\path\to\dataset"
```

The validator reports missing images, digest/dimension mismatches, invalid boxes, unsupported OCR trials, and manifest invariants. Finalization also requires a safe name and never overwrites an existing directory. See [Dataset format](DATASET-FORMAT.md).

## Setup gallery shows no maps

- Setup reads finalized datasets from `Documents\LilacMacro Datasets`.
- Dataset names must match the configured catalog names, not only directory slugs.
- Referenced frames must exist and compatible views must share client dimensions.
- Press Refresh after finalizing or renaming a dataset.
- Events currently has no implemented map definitions, so an empty Events gallery is expected.

## Placement changes do not persist

Saved files live under `%LOCALAPPDATA%\LilacMacro\placements`. Invalid routes are rejected before save. Closing the shell flushes queued writes and surfaces file-access failures. Check folder permissions and the error message; do not edit a setup JSON while LilacMacro is writing it.

## Agent dataset inspection is too large

Generate a bounded view instead of opening every frame:

```powershell
./scripts/New-AgentDatasetView.ps1 "C:\path\to\dataset"
```

Read the summary and contact sheets first. See [Agent dataset workflow](AGENT-DATASET-WORKFLOW.md).

## Live UI or Roblox verification

Agents do not operate LilacMacro or Roblox through computer-control tooling. The owner performs live checks. When reporting a problem, provide the relevant owner-captured screenshot, exact status text, dataset slug/frame, expected client size, OCR device/model, and any privacy-safe error excerpt.
