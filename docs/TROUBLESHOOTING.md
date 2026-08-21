# Troubleshooting

**Status: Current public-beta and development guidance.** Official installers are published only through this repository's GitHub Releases page.

## Windows says Unknown publisher or SmartScreen blocks setup

This is expected for the official beta installer because LilacMacro does not use an Authenticode identity certificate. Confirm the download came from `https://github.com/LeniLilac/LilacMacro/releases`, compare its SHA-256 hash with `LilacMacro-Setup.exe.sha256`, and stop if either differs. Windows may place the Run action behind **More info**. The in-app updater performs the GitHub digest, Ed25519 manifest-signature, checksum, and final cached-file checks automatically. A project signature establishes continuity with the public repository key; it does not make an unknown source safe.

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

- Enable `Deep Debug Logs` under Macro Settings, Diagnostics, or click the Dataset Builder/Runtime Lab title-bar `DEEP DEBUG` pill. New installs enable it by default.
- Complete or stop the operation. The ZIP is finalized at operation end, cancellation, shutdown, or an unhandled WPF exception.
- Use `OPEN DEEP DEBUG FOLDER` in Settings. Provisioned owner/runner UIs open `%ProgramData%\LilacMacro\Diagnostics`; an unprovisioned profile opens `%LOCALAPPDATA%\LilacMacro\diagnostics`.
- A `.deep-debug-*` directory with `finalization-error.txt` means ZIP creation failed and the uncompressed evidence was preserved.
- Retention keeps newest Deep Debug archives inside `MAX LOG STORAGE, GB`. On a provisioned machine that is one combined byte budget across the main desktop and all runners. New machines default to 3, 10, or 30 GiB according to free space, and capture reports `DEEP DEBUG LOW DISK` below 3 GiB free. Increase the byte budget or copy an important archive elsewhere before extended testing; changing it does not enable an upload.
- Archives created in the old profile-local directories before provisioning remain there and are not included in the shared cap. Delete or move those old files manually only after confirming they are no longer needed.

Read [Deep debug](DEEP-DEBUG.md) before sharing or inspecting an archive. To render bounded frames:

```powershell
./scripts/New-DeepDebugContactSheet.ps1 "path\to\deep-debug.zip"
```

## OCR is not ready

The Macro shell automatically installs the CPU OCR runtime on first launch. It uses the Windows App Installer (`winget`) to bootstrap Python 3.12 when Python is not already available, then installs the isolated runtime under `%LOCALAPPDATA%\LilacMacro\ocr`.

If automatic setup fails, confirm that the device has internet access and Windows App Installer is available. The exact setup error appears in the Macro run log and toast. Developers can repair the runtime manually with:

```powershell
py -3.12 -c "import sys; print(sys.version)"
./scripts/Setup-Ocr.ps1 -Device cpu
```

OCR lives under `%LOCALAPPDATA%\LilacMacro\ocr`; it does not use the global Python environment after setup.

## GPU setup fails

- Confirm `nvidia-smi` is available and reports compute capability 6.0 or newer. LilacMacro selects CUDA 11.8 for Pascal/GTX 10 and Volta, CUDA 12.6 for Turing through Ada, and CUDA 12.9 for Hopper or Blackwell.
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

## Roblox settings normalization fails

- Plan start and each private-server reset close Roblox before editing `GlobalBasicSettings_13.xml`. Another Roblox client in the same Windows session must not remain open or immediately relaunch during that boundary.
- A missing file means Roblox has not created settings for that Windows profile. Launch and close Roblox once, then start the plan again.
- A missing, duplicated, or type-changed required field fails closed instead of rewriting an unfamiliar format. Preserve the exact error and current file for compatibility diagnosis; do not replace the whole document with a template.
- A `.lilacmacro-backup` sibling is interrupted-replacement evidence. The next normalization validates the current file and either removes the stale backup or restores it before retrying.

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

## Keybind unavailable appears when Macro opens

- Macro start/stop defaults to F7. Settings created before 1.0.37 migrate the former F6 default to F7 so Dataset Builder or Runtime Lab can keep their F6 ownership.
- If the message names another configured key, that key is already registered globally by another process. Close the conflicting application or choose a different Macro start/stop key under Settings > Keybinds.

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

## Local instance manager is unavailable or degraded

- `Absent` means no owned provisioning journal exists. Setup is optional and ordinary application installation does not create runner accounts.
- A native compatibility failure happens before mutation. Repair reruns TermWrap's offline scanner against the exact local `termsrv.dll`. A RunDLL message that `TermWrap.dll` or one of its modules was not found indicates an incomplete native payload; the installed `native\termwrap\v0.6\x64` directory must contain both the pinned `TermWrap.dll` and `Zydis.dll`. A missing TermWrap entry point instead indicates a broken or stale probe build; install a build whose probe invokes TermWrap v0.6's published `ServiceMain` export. Do not bypass a missing required patch; inspect the reported scanner diagnostic. A Windows update changes the TermService hash and automatically invalidates prior cached evidence.
- After setup reports Ready, press `OPEN` on the intended Runner row. Install Roblox, sign into that Roblox account, and keep the viewport visible while its macro runs. Owner-profile Roblox files and login tokens are intentionally not copied.
- If `OPEN` reaches the listener but Windows asks for credentials, rejects the saved login with `0x8007052E`, or disconnects with client reason `2308` (`0x904`), run Repair. Each Runner uses a unique machine-qualified generic record under `TERMSRV/127.0.0.N` plus a separate app-namespaced repair secret; the connection port is intentionally omitted from the Credential Manager target.
- If the runner desktop opens without a full LilacMacro UI, run Repair, sign out that runner, and reopen it. Current builds register one interactive task per profile that launches `LilacMacro.exe` on logon and remote reconnect. `LilacMacro.SessionWorker` is no longer expected to remain running.
- If the main account still displays `LOCAL` immediately after initial setup, close and reopen LilacMacro. Setup seeds `%ProgramData%\LilacMacro\Configurations\shared`; the next main launch uses the same machine-DPAPI/ACL-protected configuration as every shared Runner. A separate Runner intentionally shows `SEPARATE` and uses its own root.
- Runner 1 uses `127.0.0.2:33991`, Runner 2 uses `127.0.0.3:33991`, and later slots increment the final octet. Multiple open viewports are expected. A Runner row showing `SIGNEDOUT`, `ACTIVE`, or `DISCONNECTED` describes only its Windows session, not Roblox readiness.
- `Recovery Required` means rollback or removal could not prove that every owned resource was removed or every original value restored. Keep `%ProgramData%\LilacMacro\Session\provisioning.json`, run Repair or Remove from the same signed installation, and preserve the exact unresolved-resource list.
- A profile-policy failure records a bounded failure code and exact rejected registry value before rollback. Required setup does not remove AppX packages or gate provisioning on the access-restricted Windows 11 `TaskbarDa` Widgets toggle or per-user `Software\Policies` OneDrive value; runner-only notification, suggestion, promotion, consumer-content, and ordinary `Run`-key startup suppression remain enforced. An upgrade may clear a stale recovery journal only after live cleanup verification proves the runner account, credential, task, firewall rules, runner data, and registry drift are all absent.
- A TermService stop failure should identify the rejected service immediately. Current builds stop the active Windows 11 `UmRdpService` dependency before restarting `TermService`, follow each service's wait hint for up to 60 seconds, and issue at most three total stop requests when fresh evidence shows that Windows returned the service to stable Running after an accepted stop and again advertises STOP control support. If that bounce also restarted `UmRdpService`, exact Win32 1051 permits only a bounded re-stop of that known dependent before retrying. They report the named state, accepted request count, checkpoint, and exit details. Version 1.0.45 and older can instead time out after 20 seconds with `TermService did not reach state 1`; version 1.0.46 detects the stable Running bounce but does not retry it. A successful rollback leaves status Absent so the owner can install the newer build and use Set Up again.
- Loopback isolation requires a live alternate-loopback connection plus exact enabled inbound block rules for TCP and UDP on port 33991 across every firewall profile. Repair migrates the earlier `127.0.0.1` and single-runner credentials/tasks. A connection from the machine to its own non-loopback address is not an external reachability test on Windows and must not fail setup by itself.
- `Degraded` with `windows-restart-required` means the exact loopback-only firewall policy passed, but Windows did not initialize the RDP listener within the bounded 15-second setup window. Restart Windows once, reopen LilacMacro, and run Repair. The provisioning journal is intentionally retained across that restart. Do not add Defender exclusions or expose the listener to the network. If Repair reports the same code after the restart, preserve the diagnostics and investigate the Windows Remote Desktop/Local Session Manager failure instead of retrying indefinitely.
- A listener failure paired with Local Session Manager event 17 and `0x80070005` can be caused by a stale Remote Desktop certificate whose public record remains after its private key disappeared. Setup journals that certificate baseline, replaces only certificates with a provably missing private key, and restores the original baseline on rollback or removal. A usable pre-existing RDP certificate is never replaced.
- Open Instance uses a generated profile that suppresses the server-certificate warning only for the owned `127.0.0.2` through `127.0.0.17` loopback destinations. A certificate dialog for another destination means the generated profile was not used; cancel it and run Repair rather than accepting it.
- Setup refuses active remote RDP use, existing exposure, failed exact-binary native probes, invalid hashes, unsafe preservation, and unverifiable loopback isolation. These are safety gates, not configuration suggestions.
- An isolation failure now distinguishes an invalid owned firewall scope from an RDP listener that did not accept `127.0.0.2:33991` within the bounded 15-second restart window. The exact owned block scope covers every non-loopback IPv4 and IPv6 remote source; the client connects through the alternate loopback endpoint.

Normal uninstall invokes the same cleanup path and stops if cleanup is incomplete. Owner datasets and shared configuration remain; runner accounts/profiles, endpoint credentials, tasks, isolated configuration, firewall rules, and native integration must be removed. See [Local instance manager](LOCAL-SESSION.md).

## Live UI or Roblox verification

Agents do not operate LilacMacro or Roblox through computer-control tooling. The owner performs live checks. When reporting a problem, provide the relevant owner-captured screenshot, exact status text, dataset slug/frame, expected client size, OCR device/model, and any privacy-safe error excerpt.
