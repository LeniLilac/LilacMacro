# Contributing

**Status: Current repository policy.**

LilacMacro is a private, owner-operated project. Contributions are changes made with or for the owner, not a public support process.

## Before changing code

1. Read the root [AGENTS.md](AGENTS.md), then the closest scoped `AGENTS.md` for files you will touch.
2. Read [Project status](docs/PROJECT-STATUS.md) so planned behavior is not mistaken for implemented behavior.
3. Read [Development](docs/DEVELOPMENT.md) and the topic-specific document routed by `AGENTS.md`.
4. Run `git status --short`. Existing modified and untracked files belong to the owner unless proven otherwise.

Do not discard, reset, reformat, rename, stage, or commit unrelated owner work. If a requested change overlaps an existing edit, understand and preserve the existing intent before patching it.

## Change scope

- Make the smallest cohesive change that resolves the request.
- Preserve `Core <- Windows <- App` dependency direction.
- Add a focused test for deterministic behavior or a regression whenever practical.
- Keep new production and script files at or below 500 lines and tests at or below 800 lines. Every `AGENTS.md` must remain at or below 120 lines.
- Split files by owner, policy, or lifecycle before adding a second responsibility.
- Do not hand-edit or commit `bin`, `obj`, `artifacts`, `TestResults`, local datasets, agent views, OCR environments, models, logs, or settings.

## Documentation contract

Update documentation in the same change when behavior, persistence, architecture, privacy, testing, or status changes. Use exactly these status labels:

- **Implemented:** production code exists and is covered by proportionate validation.
- **Prototype:** usable code exists, but it is an internal or incomplete surface rather than the target unattended macro.
- **Planned:** accepted design intent with no complete runtime implementation.
- **Unresolved:** a decision or design remains open.

Do not copy detailed state rules into the README. Put field-observed rules in [Game behavior](docs/GAME-BEHAVIOR.md), architecture in [Architecture](docs/ARCHITECTURE.md), and current/planned boundaries in [Project status](docs/PROJECT-STATUS.md).

## Validation

Run the commands in [Testing](docs/TESTING.md). Documentation-only work still requires the documentation validator, repository policy, and `git diff --check`. Source changes require locked restore, Release build, relevant tests, the full test suite, and formatting verification.

Live UI and Roblox behavior are owner-tested. Agents must not use computer-control tooling to operate LilacMacro or Roblox. Provide a short manual acceptance checklist when live verification remains.

## Review

Before handoff:

- inspect the complete diff;
- confirm no personal paths, credentials, captures, logs, or generated files were added;
- distinguish pre-existing failures from failures caused by the change;
- report commands run and any owner-only checks still needed.
