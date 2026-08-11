# Macro architecture

**Status: Prototype.** The Story/Raid/Challenge scheduler, shared placement/terminal path, and verified private-server Lobby reset are Prototype. Expedition, limited Event acts, persistent plans, and secret persistence remain Planned.

## Root model

Lobby is the only canonical root state. The Plan page will configure independent tasks with explicit priority; visual order means priority, not a consecutive script. After every completed or failed match attempt, the scheduler will use the configured private-server link to rejoin, verify Lobby, and reevaluate eligibility from the highest priority.

```mermaid
flowchart TD
    A["Start or terminal match"] --> B["Rejoin configured private server"]
    B --> C{"Fresh Lobby evidence?"}
    C -- "No" --> D["Escalate bounded recovery"]
    D --> B
    C -- "Yes" --> E["Evaluate tasks from highest priority"]
    E --> F{"Eligible task found?"}
    F -- "No" --> G["Wait with cancellation and reevaluate"]
    F -- "Yes" --> H["Run one mode workflow"]
    H --> I["Victory, defeat, cooldown, or bounded failure"]
    I --> B
```

Direct Repeat Stage remains useful Debug evidence but is not part of this canonical loop. Resetting through Lobby makes team selection, task priority, cooldowns, configuration changes, and recovery deterministic between matches.

## Execution target

`This desktop` is the safe default. The optional `Local runner session` target uses the same scheduler and workflow policies inside a dedicated account; the desktop app publishes an immutable snapshot and sends typed commands rather than raw input. Selection is allowed only when provisioning, exact-binary native preflight, loopback isolation, ACLs, versions, fresh capture, IPC peer identity, and the WPF-free runtime host all pass. Failure or IPC loss cancels the run and releases input. See [Optional local runner session](LOCAL-SESSION.md).

## Scheduler contract

Each task will declare:

- enabled state and priority;
- eligibility policy such as cooldown, target count, or stop condition;
- mode-specific target configuration;
- team and placement route references;
- retry and failure budget;
- completion accounting.

The scheduler evaluates a stable snapshot from highest to lowest priority, runs at most one task, records the terminal outcome, resets to Lobby, then takes a new snapshot. It must remain cancellation-aware and must never hold Roblox input while waiting for eligibility.

Every run begins by normalizing the required Roblox settings to the canonical rendered UI scale, then obtaining fresh Lobby evidence. The displayed numeric value is only a calibration input: the runtime measures panel geometry, applies bounded reciprocal feedback, and validates the result. A per-user/per-Windows-session cached candidate is a revalidated hot-path hint, not evidence. These runtime-owned invariants are never optional user settings.

## Unattended continuity contract

The public-release objective is that a configured run does not terminate because an ordinary visual, timing, Roblox, network, or task anomaly occurs while the user is away. Normal terminal conditions are an explicit user stop, invalid or incomplete user configuration detected before safe execution, an unsupported setup/environment that cannot start safely, or external process/OS termination. Safety failures stop the current action and release input; they do not silently end the scheduler.

Recovery is an indefinitely available scheduler-level escalation made from individually bounded, cancellable episodes:

1. release all input, reacquire Roblox, and expand the bounded temporal observation window using fresh evidence;
2. reopen the configured private server, restore canonical client geometry and required game/UI settings, and verify Lobby;
3. restart Roblox, reacquire it, normalize settings again, and verify Lobby;
4. quarantine the failing task with an inspectable reason, continue with the next eligible task, and periodically reconsider quarantined work under a bounded backoff policy;
5. when no task is currently safe or eligible, wait without holding input and repeat recovery until cancellation or configuration changes.

“Indefinitely available” never means infinite clicking, an unbounded wait inside one state owner, stale evidence, or bypassing a safety gate. Each observation window, input attempt, transition, restart, and cleanup remains capped. Fresh evidence must authorize every input, cancellation must interrupt every layer, and held keys/buttons must be released before any retry, restart, quarantine, or wait. Recovery history, task quarantine, and the active escalation level must remain visible in diagnostics.

## Shared modules

| Module | Planned owner |
|---|---|
| Session reset | Open the protected private-server link, reacquire Roblox, standardize client geometry, verify Lobby |
| State evidence | Combine current OCR rules with future personalized image detection and expose inspectable confidence/evidence |
| Input coordinator | Grant one workflow exclusive, cancellable Roblox input ownership and guarantee release |
| Team selection | Lobby to Unit Inventory to Teams, bounded scroll, Load/Confirm/Include flow |
| Root navigation | Lobby to Play, Events, Areas, or another verified root destination |
| Mode navigation | Map, act, difficulty, challenge type, or limited-event selection |
| Match start | Match Preview, Match Prestart, and Start Game boundary handling |
| Placement playback | Execute the selected validated route before and after Start Game |
| Terminal handling | Detect Victory, Defeat, cooldown, timeout, or fatal ambiguity and return an outcome to the scheduler |
| Persistence and reporting | Autosave Plan state, protect secrets, redact diagnostics, and optionally send bounded webhooks |

Modules exchange typed state/outcome contracts rather than clicking through one another. Every handoff requires fresh evidence for the owned state.

Placement routes reserve at most 30000 ms of authored guaranteed delay before their `Start Game` boundary. If Roblox's 60-second timer starts the match while those prestart actions are still running, the placement owner completes them in order. At the boundary, repeated fresh Start-screen absence plus independent selected-unit runtime evidence satisfies the boundary without a click, after which the same route continues into after-start actions. The timer is a state transition fallback, never a cancellation signal.

## Mode flows

All arrows below mean a verified transition, not a timed blind click.

### Story — Prototype

Lobby -> Unit Inventory -> Teams -> change team -> Play UI -> Story map -> Story act and Normal/Hard -> Match Preview -> Match Prestart -> prestart placements -> Start Game -> after-start placements -> Victory/Defeat -> private-server rejoin -> Lobby.

### Raid — Prototype

Lobby -> Unit Inventory -> Teams -> change team -> Play UI -> Raid map -> Raid act -> Match Preview -> Match Prestart -> prestart placements -> Start Game -> after-start placements -> Victory/Defeat -> private-server rejoin -> Lobby.

### Challenge — Prototype

Lobby -> Unit Inventory -> Teams -> change team -> Play UI -> Challenge type or cooldown outcome -> Match Preview -> Match Prestart -> prestart placements -> Start Game -> after-start placements -> Victory/Defeat -> private-server rejoin -> Lobby.

Challenge does not use Repeat Stage; its eligibility is reevaluated after the Lobby reset. Enabled types run in Trait, Stat, Sprite order and are attempted at most once per global half-hour epoch. A cooldown observation blocks that type until the next reset. If the same type is still cooling down after the epoch advances, that type is treated as having reached its 10/10 daily limit until UTC midnight. Availability clears the prior cooldown evidence. Other enabled types remain independently eligible.

The random Challenge map is recognized after type selection and selects the corresponding Story map's `challenge` placement route. Because team selection happens before the random map is known, all five effective Challenge routes must use one common team; disagreement fails before Roblox input.

### Expedition — Planned

Lobby -> Unit Inventory -> Teams -> change team -> Play UI -> Expedition map and difficulty -> Match Preview -> Match Prestart -> prestart placements -> Start Game -> after-start placements -> Victory/Defeat -> private-server rejoin -> Lobby.

### Event — Planned

Lobby -> Unit Inventory -> Teams -> change team -> Events -> Villain Invasion -> act -> Match Preview -> Match Prestart -> prestart placements -> Start Game -> after-start placements -> Victory/Defeat -> private-server rejoin -> Lobby.

The updated Event sidebar also exposes Boss Bounty and Guess That Unit through the same live-OCR destination selector. Their downstream flows remain unspecified, so only owner-triggered Debug selection is implemented; neither is treated as a runnable Event task yet.

## Content lifecycle

- Story, Raid, Challenge, and Expedition are permanent game modes. Their runners own only mode-specific navigation and reuse the shared team, match-start, placement, terminal, and Lobby-reset modules.
- Event acts are limited content. Each event definition must keep its identity, OCR aliases, routes, availability, and runner adapter behind one removable registration boundary instead of adding branches throughout the scheduler and UI.
- Utilities are scheduler tasks, not game modes. They use the same priority contract but remain separate from mode navigation.
- Removing limited content must delete its registration and focused tests without changing shared capture, OCR, input, placement, terminal, or scheduler services. Persisted plans will need an unavailable-content state before Plan persistence ships; they must not silently deserialize as another mode.

## State-transition safety

Every runner must:

1. enter from verified Lobby or a typed state handed off by a shared module;
2. take fresh evidence before input;
3. require the intended live target or verified relational layout;
4. cap waits, retries, scrolling, and input attempts;
5. revalidate Roblox after delays and external navigation;
6. return a typed success, cooldown, defeat, retryable failure, or fatal failure;
7. release input on cancellation and every exception path.

OCR, image detection, and timers may suggest a state. None may authorize input without the state's complete evidence contract.

## Persistence and secrets

**Prototype:** the private-server link is accepted only as an HTTPS `roblox.com` URL, retained for the current process, never written to deep-debug data, and opened through the Windows shell. **Planned:** Plan edits autosave atomically, and private-server links plus webhook URLs are protected for the current Windows user with DPAPI. Persisted secrets must remain redacted from UI diagnostics, logs, tests, and captures.

## Unresolved design work

- Macro dashboard and run controls
- Plan priority visualization, drag/reorder behavior, and task editor
- Eligibility/cooldown policies and completion counters
- Runtime detector model and confidence calibration
- Placement success and unit-state detection
- Settings grouping and protected-secret lifecycle
- Packaging, recovery after application restart, and releases
