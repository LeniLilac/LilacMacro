# LilacMacro

LilacMacro is a free, noncommercial, source-available Windows .NET/WPF app for building and running inspectable Anime Expeditions automation through ordinary screen capture and Windows input. The public beta includes the Macro shell, Plan and placement authoring, Story/Raid/Challenge/Expedition/Event and utility workflows, a Dataset Builder, and a Runtime Lab for explicit OCR/vision tests.

Download the current public beta from [GitHub Releases](https://github.com/LeniLilac/LilacMacro/releases). The latest release is [v1.0.154](https://github.com/LeniLilac/LilacMacro/releases/tag/v1.0.154). The installer is intentionally not Authenticode-signed, so Windows may show **Unknown publisher** or a Microsoft Defender SmartScreen prompt. Only use the installer from this repository's release page. Every official release includes a SHA-256 checksum and an Ed25519 project-signed manifest; the built-in updater verifies both before launch. The project signature proves continuity with the public LilacMacro key, not a verified legal identity.

## Requirements

- Windows 10 version 1903 or later, or Windows 11
- .NET SDK version pinned by [`global.json`](global.json)
- Roblox running in windowed mode for capture or Runtime Lab work
- 100% Windows display scale on the monitor containing Roblox
- Internet access when optional GPU OCR setup is requested

## Contributor quick start

```powershell
dotnet restore LilacMacro.slnx --locked-mode
dotnet build LilacMacro.slnx -c Release --no-restore
dotnet test LilacMacro.slnx -c Release --no-build
dotnet run --project src/LilacMacro.App/LilacMacro.App.csproj
```

Optional manual OCR setup for development or a pre-warmed machine:

```powershell
./scripts/Setup-Ocr.ps1 -Device cpu
# Or, on a supported NVIDIA/CUDA system:
./scripts/Setup-Ocr.ps1 -Device gpu
```

## Current status

- **Implemented:** dataset storage and validation, timed/manual capture, annotations, OCR trials, bounded agent dataset views, Roblox sizing/capture/input services, deterministic OCR policies, and the generic adaptive visual-anchor builder/matcher/profile foundation.
- **Prototype:** the four-tab macro shell, Setup placement authoring, persistent Light/Dark solid and gradient theme palettes, and owner-triggered OCR Debug transitions.
- **Prototype:** Runtime Lab and main Macro can run Story, Raid, reset-aware Challenge rotation, Expedition, Villain Invasion Events, and recurring utility tasks through authored placement playback, terminal verification, private-server Lobby reset, and priority reevaluation. Story Infinite can reset at a configured, freshly verified wave.
- **Prototype:** Discord event notifications with optional current Roblox client screenshots, configuration sharing, fixed-schema telemetry, and default-off automatic error reports are available behind separate privacy choices.
- **Prototype:** persistent plans/settings/secrets, full/compact macro layouts, and an opt-in local instance manager with loopback-only RDP, multiple standard runner accounts, one full macro UI per desktop, shared-or-isolated configuration, first-launch Roblox bootstrap, exact ownership/rollback, and uninstall cleanup. Multi-session lifecycle certification remains a beta boundary.
- **Implemented:** official-GitHub update metadata, exact six-asset release validation, GitHub and project-signature digest verification, and coordinated shutdown/relaunch of the owner plus configured runner UIs.

See the authoritative matrix in [Project status](docs/PROJECT-STATUS.md). Dataset Builder owns Capture, Review + OCR, and Datasets. Runtime Lab owns Debug and Wire Test. Both are supported internal applications built from the shared App/Core/Windows implementation.

## Safety boundary

LilacMacro uses ordinary Windows window management, Windows Graphics Capture, and Windows input. It must not inject into Roblox, read or modify Roblox process memory, hook the game, or bypass anti-cheat systems. Captures and local configuration stay outside the repository.

## Documentation

Start with the [documentation index](docs/README.md). Contributors and coding agents must also read [CONTRIBUTING.md](CONTRIBUTING.md) and [AGENTS.md](AGENTS.md).

The project is noncommercial; see [LICENSE.md](LICENSE.md), [NOTICE.md](NOTICE.md), and [PRIVACY.md](PRIVACY.md).

The local instance manager and project-signed installer boundaries are documented in [Local instance manager](docs/LOCAL-SESSION.md) and [Installer](docs/INSTALLER.md).

See [the changelog](CHANGELOG.md) for release-specific changes and known beta boundaries.
