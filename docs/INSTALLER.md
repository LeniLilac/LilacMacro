# Installer

**Status: Prototype.** The repository contains an Inno Setup definition, signed-build script, elevated setup helper, session worker, and shared WPF-free runner runtime. Supported release distribution remains blocked until the pinned native payload, production signing, and disposable-VM lifecycle certification are complete.

## Release shape

Supported releases install one signed `LilacMacro-Setup.exe` under `%ProgramFiles%\LilacMacro`. Portable publish folders remain development-only and cannot provision, coordinate updates, or reliably uninstall system resources. A GitHub release contains exactly four public assets: `LilacMacro-Setup.exe`, `LilacMacro-Setup.exe.sha256`, `LICENSE.md`, and `NOTICE.md`.

The installer contains:

- the self-contained x64 desktop application;
- the signed elevated session-setup helper;
- the signed windowless profile-policy bootstrap retained for local-instance setup compatibility;
- the pinned TermWrap payload, hash manifest, license, and notices.

Ordinary installation does not create an account, enable TermWrap, or change RDP. The owner must choose Settings, Roblox, Local instances, Set Up and approve UAC. The initial setup creates Runner 1; additional shared/separate runners use the same allowlisted elevated helper.

## Build

Install Inno Setup 6 and a Windows code-signing certificate, then run:

```powershell
./scripts/Build-Installer.ps1 -Version 1.0.0 -CertificateThumbprint CERTIFICATE_THUMBPRINT
```

For local compilation only, an explicitly marked unsigned development installer may be produced with `-UnsignedDevelopmentBuild`. It is not a release artifact.

The script performs locked restore, warning-free Release publishes for app/helper/worker, signs LilacMacro-owned executable payloads, compiles Inno Setup, signs the final installer, and writes one immutable versioned artifact directory. It never overwrites an existing artifact. The artifact directory contains the exact four release assets plus local `BUILD-INFO.txt` evidence.

After validation, `./scripts/Publish-GitHubRelease.ps1 -Version 1.0.0 -ArtifactDirectory ...` publishes the exact tagged inventory through `gh`. The script requires a clean worktree, an existing exact tag, signed payloads, and an otherwise empty release.

Run `./scripts/Test-Installer.ps1` on every platform-neutral installer change. It validates required files, TermWrap sizes/hashes, helper verbs, cleanup hooks, signing requirements, and release naming without provisioning Windows. Runtime native compatibility is established offline from the exact local TermService and TermWrap binaries before the elevated helper mutates system state.

## Upgrade and uninstall

An owner-approved in-app update downloads the exact release installer and checksum, verifies GitHub's asset digests, the manifest checksum, and trusted Authenticode, then writes an exact machine shutdown request before starting the installer with UAC. The installer rehashes its own source, requests shutdown from every recorded macro process, refuses replacement while a process remains active or uninspectable, upgrades Program Files once, repairs configured runner tasks, relaunches those tasks, clears the request, and reopens the owner UI.

An upgrade that finds an owned provisioning journal attempts an idempotent repair/migration before the upgraded runner can be used. If a prior rollback removed every owned resource but left only its journal because an already-absent scheduled task was misclassified as a cleanup failure, migration verifies the empty state and clears that orphan instead of recreating the runner. A failed optional-runner migration leaves the runner unavailable and its exact status available to the upgraded app, but it does not roll back or block the main LilacMacro upgrade. A normal uninstall still invokes `uninstall-cleanup` before deleting binaries. If cleanup fails, uninstall stops and retains the signed helper and provisioning journal. The error lists unresolved resources and can be retried after correcting the reported Windows condition.

The updater intentionally has no GitHub token path. Until the official repository or release channel is anonymously readable, in-app checks fail closed and signed installers remain manually distributed owner-test artifacts.

Installer integration testing occurs only in disposable Windows VMs. Agents do not provision the owner's machine or operate Roblox.

See [Local instance manager](LOCAL-SESSION.md) and [Testing](TESTING.md).
