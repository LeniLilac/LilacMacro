# AGENTS.md

This file applies to the entire repository. Scoped `AGENTS.md` files add rules for their subtrees.

## Mission and hard boundaries

LilacMacro is a private, Windows-only .NET/WPF utility for inspectable Roblox screen automation.

- Do not inject into Roblox, inspect or modify its memory, hook it, or bypass anti-cheat systems.
- Use ordinary Windows window management, Windows Graphics Capture, and Windows input only.
- Keep the project noncommercial and preserve its license and notices.
- Never commit credentials, private-server links, webhook URLs, logs, local models, settings, generated output, or owner datasets/captures. The only capture exception is the reviewed, allowlisted public runtime-evidence bundle governed by [docs/RUNTIME-EVIDENCE.md](docs/RUNTIME-EVIDENCE.md).
- Do not drive LilacMacro or Roblox through computer-control tooling for UI or behavior tests. The owner performs live visual and Roblox validation.

## Read before changing

- Every change: [CONTRIBUTING.md](CONTRIBUTING.md), [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md), and [docs/PROJECT-STATUS.md](docs/PROJECT-STATUS.md).
- Architecture or ownership: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
- Game navigation or OCR state behavior: [docs/GAME-BEHAVIOR.md](docs/GAME-BEHAVIOR.md).
- OCR or future detection: [docs/OCR-AND-VISION.md](docs/OCR-AND-VISION.md).
- Expedition reward OCR or reroll economics: [docs/EXPEDITION-REWARD-OPTIMIZATION.md](docs/EXPEDITION-REWARD-OPTIMIZATION.md).
- Placement work: [docs/PLACEMENT-AUTHORING.md](docs/PLACEMENT-AUTHORING.md).
- Planned scheduler/runtime: [docs/MACRO-ARCHITECTURE.md](docs/MACRO-ARCHITECTURE.md).
- Dataset work: [docs/DATASET-FORMAT.md](docs/DATASET-FORMAT.md) and [docs/AGENT-DATASET-WORKFLOW.md](docs/AGENT-DATASET-WORKFLOW.md).
- Runtime search regions or bundled detection evidence: [docs/RUNTIME-EVIDENCE.md](docs/RUNTIME-EVIDENCE.md).
- Privacy, secrets, or local data: [PRIVACY.md](PRIVACY.md).
- Deep-debug capture or diagnosis: [docs/DEEP-DEBUG.md](docs/DEEP-DEBUG.md).
- Installer or optional runner-session work: [docs/LOCAL-SESSION.md](docs/LOCAL-SESSION.md) and [docs/INSTALLER.md](docs/INSTALLER.md).
- Test scope and commands: [docs/TESTING.md](docs/TESTING.md).

## Runtime invariants

- Coordinates and requested resolutions are Roblox client-area pixels, never desktop or outer-window pixels.
- Reobserve the live Roblox client after resize, delay, focus change, or external transition before input.
- Only fresh captures and verified state evidence may authorize Debug input; static coordinates never do.
- A source control and the modal or destination it opens are separate state owners. Each must use its own dataset-labeled ROI; never merge them into one broad OCR crop or resolve duplicate labels by global screen order.
- Every semantic runtime search area must trace to one named annotation in the bundled evidence catalog. If a dataset box or note does not unambiguously define its state, layer, or intent, stop and ask the owner rather than guessing.
- Annotation and OCR rectangles use original-image, half-open coordinates and remain inside their owning image or region.
- Save manifests, settings, and placement documents atomically; never overwrite an existing finalized dataset directory.
- Keep input cancellation-aware and release every held key or mouse button on all exit paths.
- Runner setup requires an offline exact-binary native preflight, runner-profile-only policy, loopback-only isolation, and a rollback journal. Cache compatibility only by exact TermService/TermWrap hashes. Never provision the owner's machine during agent testing unless the owner explicitly authorizes it for the current task.
- Application updates may use only the unauthenticated official GitHub Releases channel. Require the exact semantic tag, release URL, four-asset inventory, sizes, direct URLs, GitHub SHA-256 digests, checksum manifest, and bounded trusted HTTPS redirects. Automatic checks are metadata-only; downloading and opening the installer require separate explicit consent, and a restart-recovered installer must be rehashed before launch.

## Architecture and status language

- Preserve dependency direction: `Core <- Windows <- Runtime <- App`; tests and tools may consume lower layers.
- Core is platform-independent, Windows owns Win32/capture/input, Runtime owns WPF-free workflow composition, and App owns WPF/lifecycle.
- Label repository claims as **Implemented**, **Prototype**, **Planned**, or **Unresolved**. Design intent must never be presented as working runtime behavior.
- Dataset Builder owns Capture/Review/Datasets; Runtime Lab owns Debug/Wire Test. Both are retained owner tools, not dead code.
- Respect repository limits: production and scripts 500 lines, tests 800 lines, every `AGENTS.md` 120 lines. Split by cohesive ownership instead of evading limits.

## Required change loop

1. Inspect `git status`; preserve unrelated owner work.
2. Make the smallest cohesive change and add focused regression coverage.
3. Run `./scripts/Test-Documentation.ps1` for documentation changes.
4. Run `./scripts/Test-RepositoryPolicy.ps1`.
5. Run locked restore, warning-free Release build, and all tests as described in [docs/TESTING.md](docs/TESTING.md).
6. Run formatting verification and `git diff --check`.
7. Review the complete intended diff and update status/behavior documentation with the code.

## Definition of done

A change is complete only when behavior and status claims match the code, relevant automated checks pass, owner-only live testing is clearly handed off, and the worktree contains no accidental files.
