# LilacMacro

LilacMacro is a private, Windows-only .NET/WPF tool for building and validating reliable Roblox screen automation. The current prototype provides a macro shell, placement authoring, a focused Dataset Builder, and a separate Runtime Lab for explicit OCR/vision transitions and flow tests. Unattended macro playback is planned.

## Requirements

- Windows 10 version 1903 or later, or Windows 11
- .NET SDK version pinned by [`global.json`](global.json)
- Roblox running in windowed mode for capture or Runtime Lab work
- Optional: Python 3.12 for local PaddleOCR

## Quick start

```powershell
dotnet restore LilacMacro.slnx --locked-mode
dotnet build LilacMacro.slnx -c Release --no-restore
dotnet test LilacMacro.slnx -c Release --no-build
dotnet run --project src/LilacMacro.App/LilacMacro.App.csproj
```

Optional OCR setup:

```powershell
./scripts/Setup-Ocr.ps1 -Device cpu
# Or, on a supported NVIDIA/CUDA system:
./scripts/Setup-Ocr.ps1 -Device gpu
```

## Current status

- **Implemented:** dataset storage and validation, timed/manual capture, annotations, OCR trials, bounded agent dataset views, Roblox sizing/capture/input services, deterministic OCR policies, and the generic adaptive visual-anchor builder/matcher/profile foundation.
- **Prototype:** the four-tab macro shell, Setup placement authoring, light/dark themes, and owner-triggered OCR Debug transitions.
- **Prototype:** Runtime Lab and main Macro can run Story, Raid, and reset-aware Challenge rotation through authored placement playback, terminal verification, private-server Lobby reset, and priority reevaluation.
- **Planned:** persistent plans and protected secrets, Expedition and limited Event runners, webhooks, and dataset/runtime integration for personalized image detection.
- **Unresolved:** Macro and Plan page design, complete Settings design, packaging, and release workflow.

See the authoritative matrix in [Project status](docs/PROJECT-STATUS.md). Dataset Builder owns Capture, Review + OCR, and Datasets. Runtime Lab owns Debug and Wire Test. Both are supported internal applications built from the shared App/Core/Windows implementation.

## Safety boundary

LilacMacro uses ordinary Windows window management, Windows Graphics Capture, and Windows input. It must not inject into Roblox, read or modify Roblox process memory, hook the game, or bypass anti-cheat systems. Captures and local configuration stay outside the repository.

## Documentation

Start with the [documentation index](docs/README.md). Contributors and coding agents must also read [CONTRIBUTING.md](CONTRIBUTING.md) and [AGENTS.md](AGENTS.md).

The project is noncommercial; see [LICENSE.md](LICENSE.md), [NOTICE.md](NOTICE.md), and [PRIVACY.md](PRIVACY.md).
