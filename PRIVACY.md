# Privacy

**Status: Current storage behavior plus clearly marked planned protections.**

LilacMacro is a private, local Windows tool. It has no application telemetry or analytics implementation. Network access occurs for explicit OCR setup/model downloads, metadata-only update checks against the official GitHub release, owner-approved update downloads, a runner's first-use Roblox login/installer bootstrap, bounded public signed-control polling, and an optional user-selected diagnostic archive upload.

## Current local data

| Data | Default location | Notes |
|---|---|---|
| Dataset images and manifests | `Documents\LilacMacro Datasets` | May contain personal or third-party game content. The owner can choose another dataset root in the developer workbench. |
| Non-secret capture settings | `%LOCALAPPDATA%\LilacMacro\settings.json` | Target size, capture schedule/mode, and dataset root. Written atomically. |
| Macro settings | Active local/shared/isolated configuration root | Non-secret configured keys, Plan definitions/selection, and Discord failure options plus DPAPI ciphertext for private-server/webhook secrets. Local roots use current-user DPAPI; ACL-restricted ProgramData roots use machine-scope DPAPI. Writes are cross-process serialized and atomic. |
| Placement setups | `%LOCALAPPDATA%\LilacMacro\placements` | Per-map route defaults and ordered placement timelines. Written atomically. |
| OCR environment | `%LOCALAPPDATA%\LilacMacro\ocr` | Private Python environment and selected runtime marker. |
| Paddle model cache | Paddle's local user cache | Model files download on first use and are not copied into the repository. |
| Crash details | `%LOCALAPPDATA%\LilacMacro\logs\latest-crash.txt` | Latest unhandled WPF exception; may contain local paths or runtime details. |
| Temporary OCR crops | OS temporary storage under `LilacMacro` | Deleted after each run when normal cleanup succeeds. |
| Agent dataset views | Dataset-local `.agent-view` directory or an explicit output directory | May reproduce private imagery and metadata; never publish or commit them. |
| Deep-debug archives | `%LOCALAPPDATA%\LilacMacro\diagnostics` | Sanitized text plus retained Roblox/crop/ROI pixels; enabled explicitly, retains at most 20 completed ZIPs. |
| Diagnostic upload identity | Active configuration root under `services\diagnostic-installation.json` | Random installation UUID used for upload quotas. It is not derived from hardware, Windows identity, Roblox identity, or IP address; a malformed existing identity fails instead of silently rotating. |
| Signed control snapshot | Active configuration root under `services\control.json` | Public maintenance, feature-disablement, schedule, and game-code data plus its signature. Each Macro UI keeps one verified last-known-good snapshot; it contains no private-server link, webhook, Roblox credential, hardware identifier, or diagnostic content. |
| Roblox client settings | `%LOCALAPPDATA%\Roblox\GlobalBasicSettings_13.xml` | At Macro plan start and private-server reset, the current Windows session closes Roblox and atomically changes only the documented UI/input allowlist. A sibling `.lilacmacro-backup` exists only until replacement is reread or a later run recovers it. |
| Optional local-runner journal | `%ProgramData%\LilacMacro\Session` | Prototype, machine-owned resource inventory, original system values, hashes, policy version, and owner/runner SIDs. Contains no password. |
| Local instance profiles and configuration | `%ProgramData%\LilacMacro\Profiles` and `Configurations` | Profile policy/receipt per runner plus shared or isolated macro configuration. Shared configuration survives instance removal; isolated configuration is removed with its runner. Roblox login data is never copied. |
| Optional runner credential | Windows Credential Manager | Prototype, random runner-account credential persisted for the loopback RDP endpoint. Never stored in JSON or passed on a command line. |
| Update metadata and installer cache | `%LOCALAPPDATA%\LilacMacro\updates` | Public release JSON plus an owner-approved installer/checksum download. The installer is checked against GitHub metadata, the release checksum manifest, and Authenticode before launch. |
| Runner first-launch marker | `%LOCALAPPDATA%\LilacMacro\runner-first-launch-v1.complete` inside each runner profile | Records only that the official Roblox login/install bootstrap completed; no Roblox credential or browser data is copied between Windows accounts. |

## Data handling rules

- Keep datasets, screenshots, OCR crops, agent views, logs, settings, and models out of Git.
- Inspect only datasets the owner explicitly places in scope.
- Do not upload captures or logs except through the explicit LilacMacro Services diagnostic flow described below.
- Treat deep-debug ZIPs as private captures even though known text secrets and profile paths are redacted; game pixels can still contain personal data.
- Redact local usernames, private-server links, webhook URLs, and other secrets from issues, test fixtures, diagnostics, and documentation.
- Delete local datasets and caches through ordinary Windows file management when no longer needed; LilacMacro does not currently provide a data-erasure workflow.
- Optional runner setup is runner-profile-only. It must not remove packages, startup entries, notifications, or settings from the owner's profile or all users.
- Treat the provisioning journal as sensitive system metadata even though it contains no credential. Access is restricted to the owner, SYSTEM, and Administrators.
- Roblox settings normalization is restricted to the current Windows profile and preserves unknown fields, identifiers, graphics quality, FPS, audio, window placement, and unrelated preferences. It never reads or writes Roblox process memory.

## Optional diagnostic uploads

**Implemented:** Diagnostic upload consent defaults off. Enabling it only exposes an `UPLOAD ARCHIVE` action; LilacMacro never discovers, queues, or uploads an archive automatically. Every transfer requires the user to choose one local ZIP through the file picker. Disabling the option blocks new uploads, and Cancel interrupts hashing or transfer work.

The client hashes the complete archive before requesting a grant, binds the exact filename, size, SHA-256, kind, application version, random installation UUID, and explicit-consent flag, rejects redirects, and accepts storage grants only for the official Backblaze B2 S3 origin and diagnostic object prefix. Single and multipart transfers bind exact byte lengths and SHA-256 checksums. Authorization tokens and signed storage URLs are session-only and are never logged or persisted.

Routine archives through 3 GiB are retained for 72 hours after server verification. Archives over 3 GiB through the absolute 30 GiB limit require a short-lived administrator grant, must be accepted within 30 minutes, and otherwise enter deletion; accepted retention defaults to 72 hours and may be extended to at most seven days. LilacMacro Services applies upload quotas using the random installation UUID and a server-secret network pseudonym rather than storing a raw IP address. Roblox pixels can still contain account, chat, inventory, or other personal game data, so users should inspect the selected archive before uploading it.

Settings shows the random Installation ID so a user can request a large-file grant. The user sends
that ID, the archive's exact byte size, and archive kind to an administrator, then pastes the returned
30-minute grant into the Large File Grant field before selecting the ZIP. The ID is random and is not
a hardware identifier. The grant is short-lived, bound to that exact request, and should not be
shared publicly.

## Secret storage

**Prototype:** Settings displays the Roblox private-server link as editable account-routing configuration and masks the Discord webhook. Both values store only DPAPI ciphertext. Private-server navigation parses the value in memory, launches a reduced `roblox://` target through the registered protocol, and never writes the plaintext link to command output, settings JSON, or diagnostics. Test Webhook accepts only the official Discord HTTPS execute-webhook shape, disables redirects and mentions, uses a bounded timeout, and never includes the secret URL or response body in surfaced failures. If DPAPI decryption fails, the unusable value is discarded in memory rather than exposed or treated as configured; explicit recovery UX remains Planned.

## External software

OCR setup installs pinned PaddlePaddle and PaddleOCR packages into the local LilacMacro environment. Those packages and their model downloads are governed by their own licenses and services. See [NOTICE.md](NOTICE.md) and the files under [`licenses`](licenses/) for bundled attributions.

The prototype installer also bundles pinned TermWrap v0.6 binaries and notices. It never downloads or replaces native session components at runtime. Managed instances use only loopback RDP and disable clipboard, drive, printer, smart-card, microphone, and device redirection. See [Local instance manager](docs/LOCAL-SESSION.md).

On a managed runner's first full-UI launch, LilacMacro opens the official Roblox Login page. If Roblox is absent, it follows only the official `roblox.com` download endpoint to a bounded `rbxcdn.com` installer URL, requires a trusted Authenticode signature, and starts the visible installer. LilacMacro never enters, stores, or transfers Roblox credentials.
