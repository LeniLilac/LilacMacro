# Local instance manager

**Status: Prototype and unavailable by default.** The owner can opt into an installer-owned loopback RDP foundation and create up to 16 local LilacMacro instances. Release distribution remains blocked on signing, disposable-VM lifecycle certification, and owner live acceptance.

## Model

LilacMacro does not remote-control a hidden worker. Every configured instance is a standard Windows account with its own visible RDP desktop, Roblox installation/login, and full `LilacMacro.exe` UI. Macro work executes locally in that UI and uses the same ordinary Windows capture/input path as **This desktop**.

Settings, Roblox lists the main UI plus Runner 1, Runner 2, and later profiles. The owner can set up the foundation, add a shared or separate runner, open a runner viewport, repair the machine integration, remove one runner, or remove the complete foundation. Managed runner UIs display the same list read-only; only the provisioning owner may mutate it.

Each runner receives:

- a non-administrator `LilacMacroRunner...` account with only Remote Desktop Users membership;
- one unique loopback destination (`127.0.0.2` for Runner 1, `127.0.0.3` for Runner 2, through slot 16) on the owned port;
- one endpoint-specific Credential Manager record and one app-namespaced repair secret;
- one uniquely named interactive scheduled task that launches the installed full UI on logon and remote reconnect;
- one ACL-restricted profile-policy directory under `%ProgramData%\LilacMacro\Profiles`.

Removing the final runner leaves the instance-manager foundation installed with zero configured runners. The empty manifest is preserved as empty, so the removed runner does not reappear as a legacy Runner 1; `ADD SHARED` or `ADD SEPARATE` creates a new Runner 1, while `REMOVE ALL` removes the remaining machine-level foundation.

Runner policy hides the globally provisioned desktop icons only inside runner accounts, clears wallpaper, and uses a pure-black desktop background. It does not remove owner shortcuts or change the owner's personalization.
The same runner-scoped policy suppresses Windows' first-logon privacy experience and Microsoft Edge's first-run page so the official Roblox login bootstrap is immediately usable. Because Windows protects each user's `Software\Policies` branch from standard-user writes, the elevated setup helper writes only those two exact allowlisted values into the runner's loaded user hive; the controlled runner pass continues to own ordinary personalization. Repair reapplies both layers to existing runners.

The pinned TermWrap listener, firewall rules, certificate baseline, and rollback journal are machine-level resources shared by all managed instances. Adding or removing Runner 2 does not restart or duplicate that foundation.

## Configuration modes

`+ ADD SHARED` points the runner at `%ProgramData%\LilacMacro\Configurations\shared`. The main account switches to that root on its next launch, so Plan definitions, selected Plan, keybinds, private-server/webhook settings, and placements are common to the main UI and every shared runner. Existing owner settings and placements seed the shared root once. Secret values are reprotected with machine-scope DPAPI and the directory ACL admits only the provisioning owner, selected shared runners, SYSTEM, and Administrators. Macro-settings writes take a cross-process file lock before atomic replacement.

`+ ADD SEPARATE` gives the runner `%ProgramData%\LilacMacro\Configurations\runner-N`, with an ACL limited to the owner, that runner, SYSTEM, and Administrators. Its Plan, keybind, secret, and placement edits do not affect any other instance. Runtime state, OCR environments, UI-scale caches, logs, diagnostics, Roblox settings, and Roblox credentials always remain inside the Windows account/session that owns the UI; they are never shared configuration.

Do not edit the same shared Plan or placement simultaneously in two UIs. Writes are serialized and atomic, but a later complete editor snapshot intentionally wins over an earlier snapshot.

## Provisioning

`LilacMacro.SessionSetup.exe` is the only elevated component. It accepts the allowlisted machine actions `install`, `repair`, `remove`, and `uninstall-cleanup`, the bounded profile additions `add-shared` and `add-isolated`, and `remove-profile <runner-id>` with a validated identifier. Ordinary application launch is never elevated.

Initial setup:

1. verifies the signed/pinned native payload and runs the isolated exact-binary TermWrap compatibility probe;
2. journals the original registry and Remote Desktop certificate state before mutation;
3. creates Runner 1, applies the minimal runner-profile policy, and stores endpoint-specific credentials;
4. installs and verifies exact all-profile TCP/UDP inbound block rules for the owned port, applies the loopback RDP integration, and waits for `127.0.0.2:33991`;
5. registers the Runner 1 full-UI task, removes the legacy windowless-worker task during migration, secures configuration/profile roots, and records the instance manager Ready.

Repair revalidates the exact native binaries and machine isolation, rotates/reconciles every configured account credential, reapplies each profile policy, and registers one full-UI task per profile. A Windows update or payload change invalidates cached compatibility evidence. The bounded `windows-restart-required` state remains available when exact firewall policy passes but the listener has not initialized yet.

Any failure after mutation runs the rollback journal. Cleanup attempts every independently owned profile and machine resource even when an earlier step fails. Recovery Required retains the journal and helper so repair/removal can be retried. Owner datasets and shared configuration survive complete removal; isolated runner configuration is removed with that runner.

`LilacMacro.SessionWorker.exe` remains packaged as a noninteractive, one-shot runner-profile policy bootstrap for upgrade compatibility. No scheduled task launches its legacy named-pipe runtime, and the main UI does not publish snapshots or proxy commands to another session.

## Running instances

`OPEN` writes a secret-free per-profile RDP file and starts `mstsc` only for the selected profile's loopback alias. Each runner independently persists `1920 x 1080` or `1366 x 768`; the generated windowed RDP profile requests that fixed desktop size, enables smart viewport scaling, and disables dynamic resolution. Before launch, the owner UI reconstructs the endpoint credential from the app-namespaced repair secret so stale RDP cache records self-heal without exposing the generated password. The profile suppresses the certificate-name warning that Windows otherwise repeats for each `127.0.0.x` alias, while retaining CredSSP account authentication and disabling clipboard, drive, printer, COM-port, smart-card, WebAuthn, and device redirection. This exception is bounded to the locally generated loopback profile; no remote destination is accepted. The scheduled task starts the full macro UI in that logged-on desktop. A session-local runner mutex prevents Repair or overlapping logon/reconnect triggers from opening a second UI for the same runner while still allowing different runners concurrently. Install Roblox and sign into the intended Roblox account separately in each Windows runner profile; owner-profile Roblox files and login tokens are intentionally not copied.

On the runner's first full-UI launch, LilacMacro opens the official Roblox Login page. When no Roblox player installation is visible to that account, it downloads the official Windows installer through a bounded Roblox-to-RBXCDN HTTPS redirect, validates trusted Authenticode, and runs the visible installer. The owner still completes installation/login in that viewport; LilacMacro never enters credentials. A per-profile marker prevents repeating the bootstrap after Roblox installation is verified.

The RDP viewport must remain visibly connected while that instance captures Roblox. Minimizing, disconnecting, locking, or signing out can make Windows Graphics Capture unavailable; the local macro then follows its normal bounded recovery rather than accepting stale pixels. Multiple runner accounts may remain connected and run concurrently because each session owns its own Roblox process, input gate, macro UI, OCR state, diagnostics, and unique Credential Manager endpoint.

## Coordinated updates

Only **This desktop** checks and downloads updates. Every UI runs the single Program Files installation, so the owner approves one installer rather than updating each Windows account separately. The owner UI verifies the exact GitHub digests, project-signed manifest, and checksum, records the exact active macro PIDs and configured runners, then starts the installer. Each desktop observes a machine-scoped shutdown request and closes through the normal cancellation path. Installation fails closed if a recorded process remains active or cannot be inspected. After replacement and repair, the helper re-registers and launches each configured runner task; the installer reopens the owner UI.

Automatic startup checks fetch metadata only. Download and install are separate owner actions. Runner UIs show that updates are coordinated from **This desktop** and cannot initiate them.

## Release certification

Windows build numbers are not statically allowlisted. Compatibility is decided from the exact local binaries by the offline probe. Disposable Windows 10/11 x64 VM certification remains mandatory for clean install, legacy migration, Runner 1/Runner 2 concurrent startup, shared/separate configuration, repair after Windows Update, individual removal, rollback, full uninstall, and non-loopback isolation.

See [Installer](INSTALLER.md), [Architecture](ARCHITECTURE.md), [Privacy](../PRIVACY.md), and [Troubleshooting](TROUBLESHOOTING.md).
