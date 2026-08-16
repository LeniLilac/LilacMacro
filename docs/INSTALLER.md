# Installer

**Status: Implemented.** The installer and updater are entering public beta. The repository contains the Inno Setup definition, project-signed release workflow, elevated setup helper, session worker, and shared WPF-free runner runtime. Multi-session installation and cleanup remain beta features that require owner-supervised disposable-VM acceptance before broader confidence.

## Release shape and trust

Official releases install one `LilacMacro-Setup.exe` under `%ProgramFiles%\LilacMacro`. Portable publish folders remain development-only and cannot provision, coordinate updates, or reliably uninstall system resources. A GitHub release contains exactly six public assets:

- `LilacMacro-Setup.exe`
- `LilacMacro-Setup.exe.sha256`
- `LilacMacro-Release.json`
- `LilacMacro-Release.sig`
- `LICENSE.md`
- `NOTICE.md`

LilacMacro does not use an Authenticode identity certificate. Windows therefore reports **Unknown publisher**, and Microsoft Defender SmartScreen may require the user to inspect **More info** before deciding whether to run the installer. That is expected for an official beta artifact; it must not be described as a Windows-verified publisher.

Release integrity is instead bound to a repository-controlled Ed25519 key. `eng/release-trust.json` publishes the key ID and public key, while the private key exists only as the protected `LILACMACRO_RELEASE_SIGNING_PRIVATE_KEY` GitHub Actions secret. The release workflow signs the exact raw manifest bytes. The manifest binds the tag, installer filename, byte length, and SHA-256 digest. The separate checksum is convenient for manual verification. This project signature proves continuity with the published LilacMacro key, not the contributor's legal identity and not that the program is bug-free.

The installer contains:

- the self-contained x64 desktop application;
- the elevated session-setup helper;
- the windowless profile-policy bootstrap retained for local-instance setup compatibility;
- the pinned TermWrap payload, hash manifest, license, and notices.

The versioned native payload is install-once. Upgrades do not overwrite loaded native files; Repair verifies their exact pinned hashes and fails closed on drift. A future native payload must use a new versioned directory and an explicitly certified migration.

Ordinary installation does not create an account, enable TermWrap, or change RDP. The owner must choose Settings, Roblox, Local instances, Set Up and approve UAC. The initial setup creates Runner 1; additional shared/separate runners use the same allowlisted elevated helper.

The installer displays `TERMS.md` before installation while preserving the repository-license page, and installs both `TERMS.md` and `PRIVACY.md` beside the executable. The application—not the elevated installer—owns the versioned first-run privacy choices so the same screen applies to new installs, existing profiles receiving a changed notice, and managed configuration roots. No choice-covered request occurs before that screen is saved.

## Build and publish

Local installer validation requires Inno Setup 6:

```powershell
./scripts/Build-Installer.ps1 -Version 1.0.140 -UnsignedDevelopmentBuild
```

That switch deliberately omits the release manifest and signature, records `release_manifest_signed=false`, and cannot produce a publishable release.

Official artifacts are built by `.github/workflows/release.yml` from the current `main` commit. The workflow pins its GitHub Actions and Inno Setup dependencies, requires the input version to match `Directory.Build.props`, compares the committed release key with the public repository variable, and runs the complete validation suite in a read-only build job with no persisted GitHub credential. A second `release-signing` environment job receives only the immutable candidate and exposes the private key only to the in-process manifest finalizer; it never runs restore, build, installer, or GitHub tooling while the key exists. A third job receives only the signed result, verifies its source commit and signature, creates the annotated tag, and publishes the exact six-asset inventory. Public beta releases use the normal supported update channel while their title and notes clearly retain the beta label. Never copy the private key into a local file, command line, artifact, log, or repository secret other than the protected `release-signing` environment secret.

`scripts/Build-Installer.ps1` requires a clean source tree for release candidates, records the exact source commit, performs locked restore, publishes app/helper/worker, compiles Inno Setup, hashes the installer, and creates—but never signs—the canonical manifest. `scripts/Finalize-ReleaseArtifact.ps1` validates that candidate and signs only the exact raw manifest bytes. It does not launch a child process while the private key is present. The scripts write one immutable versioned artifact directory and refuse to overwrite an existing one. `BUILD-INFO.txt` is local CI evidence and is not a release asset.

`scripts/Publish-GitHubRelease.ps1` requires a clean worktree, exact existing tag, official build metadata, all six assets, a matching checksum/manifest, and a valid Ed25519 signature before calling GitHub. `scripts/Test-Installer.ps1` validates required files, TermWrap sizes/hashes, helper verbs, cleanup hooks, release trust, and naming without provisioning Windows.

## Upgrade and uninstall

An owner-approved in-app update downloads the exact six-asset release metadata, then downloads the manifest, signature, checksum, and installer from exact official GitHub URLs without a token. It requires GitHub's SHA-256 digest for every asset, verifies the Ed25519 manifest signature and its tag/name/size binding, requires the installer hash to match both the signed manifest and checksum, and rehashes the cached installer immediately before launch. Any missing, changed, redirected, private, draft, or disallowed prerelease artifact fails closed.

The elevated installer creates a dedicated non-reparse ProgramData control directory, replaces inheritance with an Administrators/SYSTEM-write and Users-read ACL, and refuses an unsafe precreated request path. It rehashes its own source, requests shutdown from every recorded macro process, refuses replacement while a process remains active or uninspectable, upgrades Program Files once, validates and repairs configured runner tasks, relaunches those tasks only after successful repair, clears the request, and reopens the owner UI. A manually opened installer writes the same graceful machine shutdown request and gives each owner/runner UI five seconds to flush. Because Windows Restart Manager cannot close a detected process across sessions and treats the install-once TermWrap payload as owned by Remote Desktop Services, setup instead applies an exact four-product-image `taskkill` fallback before extraction. Uninstall uses the same exact product list: it requests a non-force close, waits five seconds for UI flush, then applies the bounded force-close fallback before runner cleanup. It aborts on any unexpected shutdown result and never terminates Roblox, Remote Desktop Services, or unrelated processes.

An upgrade that finds an owned provisioning journal attempts an idempotent repair/migration before the upgraded runner can be used. If a prior rollback removed every owned resource but left only its journal, migration verifies the empty state and clears that orphan instead of recreating the runner. A failed optional-runner migration leaves that runner unavailable and its exact status visible, but it does not roll back the main LilacMacro upgrade. Normal uninstall invokes `uninstall-cleanup` before deleting binaries. If cleanup fails, uninstall stops, reports unresolved resources, and can be retried after the reported Windows condition is corrected.

Installer integration testing occurs only in disposable Windows VMs. Agents do not provision the owner's machine or operate Roblox.

See [Local instance manager](LOCAL-SESSION.md), [Testing](TESTING.md), and [Security](../SECURITY.md).
