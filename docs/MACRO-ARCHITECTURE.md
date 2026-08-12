# Macro architecture

**Status: Prototype.** The Story/Raid/Challenge scheduler, shared placement/terminal path, verified private-server Lobby reset, persistent Plan authoring, and DPAPI secret persistence are Prototype. Expedition and limited Event acts remain Planned.

## Root model

Lobby is the canonical entry and task-change root state. The Plan page configures independent tasks with explicit priority; visual order means priority, not a consecutive script. After every completed or failed match attempt, the scheduler records the outcome and reevaluates eligibility from the highest priority. An exact same-task Story/Raid selection may continue through Repeat Stage; any task, act, or mode change returns through verified Lobby.

```mermaid
flowchart TD
    A["Plan start"] --> B["Rejoin configured private server"]
    B --> C{"Fresh Lobby evidence?"}
    C -- "No" --> D["Escalate bounded recovery"]
    D --> B
    C -- "Yes" --> E["Evaluate tasks from highest priority"]
    E --> F{"Eligible task found?"}
    F -- "No" --> G["Wait with cancellation and reevaluate"]
    F -- "Yes" --> H["Run one mode workflow"]
    H --> I["Victory, defeat, cooldown, or bounded failure"]
    I --> J["Record outcome and reevaluate priority"]
    J -- "Exact same Story/Raid task" --> K["Verify and click Repeat Stage"]
    K --> L["Verify Match Prestart; retain team and camera"]
    L --> H
    J -- "Changed, complete, Challenge, or Repeat unavailable" --> B
```

Repeat Stage is a bounded fast path, not a competing scheduler. It is authorized only after a typed terminal outcome and a fresh priority decision select the exact same Story/Raid task. The continuation verifies Match Prestart and reruns placements without team selection or camera alignment because Roblox retains both. Resetting through Lobby remains mandatory for task/act/mode changes, completion, Challenge, and Repeat failure.

## Execution target

Every macro UI executes on its own Windows desktop. The main account can run directly, while each optional runner account receives the same full UI inside a loopback-only RDP session. The main UI manages accounts/viewports but does not publish work or forward input to them. Shared runners point at one ACL-restricted configuration root; separate runners receive independent roots. Each instance independently verifies Roblox, capture freshness, Lobby, settings/UI scale, and input ownership before acting. See [Local instance manager](LOCAL-SESSION.md).

## Scheduler contract

Each task will declare:

- enabled state and priority;
- eligibility policy such as cooldown, target count, or stop condition;
- mode-specific target configuration;
- team and placement route references;
- retry and failure budget;
- completion accounting.

The scheduler evaluates a stable snapshot from highest to lowest priority, runs at most one task, records the terminal outcome, and immediately reevaluates priority. If the same supported task remains selected it may use Repeat Stage; otherwise it resets to Lobby before the next task. It must remain cancellation-aware and must never hold Roblox input while waiting for eligibility.

Every plan start and private-server reset begins by closing Roblox in the owning Windows session, atomically normalizing the explicit UI/input allowlist in that profile's Roblox settings file, opening the validated private-server link, and obtaining fresh Lobby evidence. It then normalizes the rendered UI scale. The displayed numeric value is only a calibration input: the runtime measures panel geometry, applies bounded reciprocal feedback, and validates the result. A per-user/per-Windows-session cached candidate is a revalidated hot-path hint, not evidence. These runtime-owned invariants are never optional user settings.

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
| Persistence and reporting | Atomically autosave Plan state; protect secrets, redact diagnostics, and optionally send bounded webhooks |

Modules exchange typed state/outcome contracts rather than clicking through one another. Every handoff requires fresh evidence for the owned state.

Placement routes reserve at most 30000 ms of authored guaranteed delay before their `Start Game` boundary. If Roblox's 60-second timer starts the match while those prestart actions are still running, the placement owner completes them in order. At the boundary, repeated fresh Start-screen absence plus independent selected-unit runtime evidence satisfies the boundary without a click, after which the same route continues into after-start actions. The timer is a state transition fallback, never a cancellation signal.

## Mode flows

All arrows below mean a verified transition, not a timed blind click.

### Story — Prototype

Lobby -> Unit Inventory -> Teams -> change team -> Play UI -> Story map -> Story act and Normal/Hard -> Match Preview -> Match Prestart -> prestart placements -> Start Game -> after-start placements -> Victory/Defeat -> exact same task: Repeat Stage -> verified Match Prestart with retained team/camera; otherwise private-server rejoin -> Lobby.

### Raid — Prototype

Lobby -> Unit Inventory -> Teams -> change team -> Play UI -> Raid map -> Raid act -> Match Preview -> Match Prestart -> prestart placements -> Start Game -> after-start placements -> Victory/Defeat -> exact same task: Repeat Stage -> verified Match Prestart with retained team/camera; otherwise private-server rejoin -> Lobby.

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
- Removing limited content must delete its registration and focused tests without changing shared capture, OCR, input, placement, terminal, or scheduler services. Persisted plan modes use exact stable names and reject unknown values rather than silently deserializing as another mode; an unavailable-content state is still required before a persisted limited Event task can outlive its registration.

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

**Prototype:** Plan edits, selection, and Discord failure options autosave through a serialized queue to a schema-versioned atomic settings file; invalid plan payloads fail closed to the built-in defaults without discarding separately valid settings. Private-server and webhook values are masked in Settings and persisted only as current-user DPAPI ciphertext. Validated web/share links are reduced to bounded share/link codes and launched through the registered `roblox://` protocol without a browser intermediary. Test Webhook performs a bounded, mention-free delivery to the validated Discord endpoint. Terminal failure notifications always include failure details; runtime-triggered delivery remains Planned. Persisted secrets remain redacted from diagnostics, logs, tests, and captures. **Planned:** runtime webhook delivery and explicit recovery UX for DPAPI data that cannot be decrypted.

## Unresolved design work

- Macro dashboard and run controls
- Eligibility/cooldown policies and completion counters
- Runtime detector model and confidence calibration
- Placement success and unit-state detection
- Settings grouping and protected-secret lifecycle
- Packaging, recovery after application restart, and releases
