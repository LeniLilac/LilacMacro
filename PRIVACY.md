# Privacy

**Status: Current storage behavior plus clearly marked planned protections.**

LilacMacro is a private, local Windows tool. It has no application telemetry or analytics implementation. Network access occurs only when the owner explicitly installs OCR dependencies or when Paddle downloads model files on first use.

## Current local data

| Data | Default location | Notes |
|---|---|---|
| Dataset images and manifests | `Documents\LilacMacro Datasets` | May contain personal or third-party game content. The owner can choose another dataset root in the developer workbench. |
| Non-secret capture settings | `%LOCALAPPDATA%\LilacMacro\settings.json` | Target size, capture schedule/mode, and dataset root. Written atomically. |
| Macro keybind settings | `%LOCALAPPDATA%\LilacMacro\macro-settings.json` | Non-secret configured keys shared across versioned artifacts. Written atomically. |
| Placement setups | `%LOCALAPPDATA%\LilacMacro\placements` | Per-map route defaults and ordered placement timelines. Written atomically. |
| OCR environment | `%LOCALAPPDATA%\LilacMacro\ocr` | Private Python environment and selected runtime marker. |
| Paddle model cache | Paddle's local user cache | Model files download on first use and are not copied into the repository. |
| Crash details | `%LOCALAPPDATA%\LilacMacro\logs\latest-crash.txt` | Latest unhandled WPF exception; may contain local paths or runtime details. |
| Temporary OCR crops | OS temporary storage under `LilacMacro` | Deleted after each run when normal cleanup succeeds. |
| Agent dataset views | Dataset-local `.agent-view` directory or an explicit output directory | May reproduce private imagery and metadata; never publish or commit them. |
| Deep-debug archives | `%LOCALAPPDATA%\LilacMacro\diagnostics` | Sanitized text plus retained Roblox/crop/ROI pixels; enabled explicitly, retains at most 20 completed ZIPs. |
| Optional local-runner journal | `%ProgramData%\LilacMacro\Session` | Prototype, machine-owned resource inventory, original system values, hashes, policy version, and owner/runner SIDs. Contains no password. |
| Optional runner snapshot and policy | `%ProgramData%\LilacMacro\Runner` | Prototype, ACL-restricted immutable non-secret runtime snapshot plus runner policy/receipt. Removed with the runner. |
| Optional runner credential | Windows Credential Manager | Prototype, random runner-account credential persisted for the loopback RDP endpoint. Never stored in JSON or passed on a command line. |

## Data handling rules

- Keep datasets, screenshots, OCR crops, agent views, logs, settings, and models out of Git.
- Inspect only datasets the owner explicitly places in scope.
- Do not upload captures or logs to third-party services.
- Treat deep-debug ZIPs as private captures even though known text secrets and profile paths are redacted; game pixels can still contain personal data.
- Redact local usernames, private-server links, webhook URLs, and other secrets from issues, test fixtures, diagnostics, and documentation.
- Delete local datasets and caches through ordinary Windows file management when no longer needed; LilacMacro does not currently provide a data-erasure workflow.
- Optional runner setup is runner-profile-only. It must not remove packages, startup entries, notifications, or settings from the owner's profile or all users.
- Treat the provisioning journal as sensitive system metadata even though it contains no credential. Access is restricted to the owner, SYSTEM, and Administrators.

## Planned secret storage

**Planned:** Settings will eventually accept Roblox private-server links and webhook URLs. Those values are not implemented today. Before they are introduced, they must be encrypted for the current Windows user with DPAPI, excluded from logs and captures, and exposed only through redacted UI and diagnostics. Private-server navigation must not leak the link through command output or persisted plain text.

## External software

OCR setup installs pinned PaddlePaddle and PaddleOCR packages into the local LilacMacro environment. Those packages and their model downloads are governed by their own licenses and services. See [NOTICE.md](NOTICE.md) and the files under [`licenses`](licenses/) for bundled attributions.

The prototype installer also bundles pinned TermWrap v0.6 binaries and notices. It never downloads or replaces native session components at runtime. The optional runner uses only loopback RDP and disables clipboard, drive, printer, smart-card, microphone, and device redirection. See [Optional local runner session](docs/LOCAL-SESSION.md).
