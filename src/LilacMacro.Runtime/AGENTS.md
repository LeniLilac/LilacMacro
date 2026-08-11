# Runtime agent instructions

These rules apply to the shared headless workflow runtime.

- Keep this project WPF-free. It may compose Core and Windows services and link reusable App sources only when those sources have no UI dependency.
- Keep runtime policy identical between the desktop macro and `LilacMacro.SessionWorker.exe`; do not fork game behavior for the runner session.
- Accept immutable, validated `RunnerRuntimeSnapshot` data. Never read the owner's profile or datasets directly from the runner account.
- Treat private-server links as ephemeral pipe payloads. Never write, log, archive, or add them to runtime snapshots.
- Preserve one Roblox-input owner, cancellation awareness, bounded waits, fresh evidence, and guaranteed input release on every exit path.
- Materialized runner files belong below the app-owned ProgramData runtime root. Use atomic replacement and revision-specific directories.
- Unsupported task modes, missing placements, missing bindings, stale contexts, or unavailable OCR must fail visibly before input.
- Keep scheduler behavior rooted at verified Lobby and reevaluate priority after every terminal match.
- Agents must not drive LilacMacro or Roblox through computer-control tooling. Live visual and Roblox acceptance belongs to the owner.
- Follow the repository validation loop in [`../../docs/TESTING.md`](../../docs/TESTING.md).
