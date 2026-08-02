# AGENTS.md

This file applies to the entire repository.

## Mission and boundaries

LilacMacro is a Windows-only .NET/WPF utility for preparing reliable Roblox screen-automation datasets.

- Do not inject into Roblox, inspect its memory, hook it, or bypass anti-cheat systems.
- Use ordinary Windows window management and Windows Graphics Capture only.
- Keep the project noncommercial and preserve license notices.
- Never commit user captures, datasets, logs, credentials, local settings, or generated build output.

## Runtime invariants

- All requested resolutions are Roblox client-area pixels, not outer-window or desktop pixels.
- Verify the live client size after every resize; a requested coordinate never substitutes for observation.
- Capture only fresh frames from the verified Roblox window and record the actual size with every frame.
- Annotation rectangles use original-image pixel coordinates, clamp to image bounds, and ignore tiny drags.
- Save dataset manifests atomically. Captures remain drafts until the owner gives the dataset a name and finalizes it.
- Do not overwrite an existing dataset directory.

## Architecture

Preserve `Core <- Windows <- App`.

- Core owns models, validation, schedules, naming, and persistence contracts without WPF or Win32.
- Windows owns Roblox discovery, client sizing, display geometry, and window capture.
- App owns WPF composition and user interaction.
- `scripts/Test-RepositoryPolicy.ps1` enforces 500 lines for production C#/XAML/Python and repository scripts, 800 for tests, and 120 for this file. Debt exceptions are exact ceilings that must fall whenever the file shrinks.

## Required change loop

1. Inspect `git status` and preserve unrelated work.
2. Make the smallest cohesive change.
3. Add focused regression coverage.
4. Run `./scripts/Test-RepositoryPolicy.ps1`.
5. Run `dotnet build LilacMacro.slnx -c Release` and `dotnet test LilacMacro.slnx -c Release --no-build`.
6. Run `dotnet format LilacMacro.slnx --verify-no-changes` and `git diff --check`.
7. Review the complete diff and update documentation when behavior changes.
