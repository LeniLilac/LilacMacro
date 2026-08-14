# Expedition reward optimization

**Status: Prototype runtime and field evidence.** Compact enlarged-ROI OCR, typed target selection, per-difficulty local sampling, device-timed dynamic thresholds, and verified restart reroll transitions are wired. Learned decisions remain provisional until broader post-update evidence and owner live acceptance are supplied.

## Goal

Expedition routes expose a randomized reward pool before a run. Restarting from the route screen rerolls that pool, but every reroll delays the next completed run. The intended optimizer chooses whether to accept the visible pool or reroll it to maximize one selected resource per unit of real time:

- Fuel Cell;
- Equipment Scrap;
- Equipment Reroll;
- Equipment Lock;
- Expedition Coin.

The optimizer is generic. Resource definitions, observed quantity distributions, run duration, and reroll duration are data; no resource gets a dedicated OCR or navigation implementation.

## Field evidence

Dataset names are relative to `Documents\LilacMacro Datasets` and are private local evidence that must not be committed.

### Standard-scale reward pool

`expedition-route-reward-pool-20260809-195831` contains 414 intentional reward-pool captures at the standard Roblox UI scale. Frames 415-419 are accidental finish-capture input and are excluded from analysis.

Owner timing observations:

- restarting immediately to obtain another pool costs approximately 9-10 seconds;
- extracting after one boss node takes approximately 10 minutes.

For an observed target quantity `X`, threshold `T`, acceptance probability `p(T)`, reroll time `R`, and run time `M`, compare thresholds by estimated reward throughput:

```text
accepted reward rate(T)
  = E[X | X >= T]
    / (M + R * (1 / p(T) - 1))
```

The 414-frame sample produced these historical accept-at-or-above estimates:

| Optimization target | Provisional minimum |
|---|---:|
| Fuel Cell | 34 |
| Equipment Scrap | 35 |
| Equipment Reroll | 4 |
| Equipment Lock | 5 |
| Expedition Coin | 34 |

These values describe this sample and the timing assumptions above. They are not constants in workflow code. Runtime recomputes the threshold from the selected difficulty's local distribution and the current OCR device's rolling end-to-end reroll time.

### UI-scale spot check

`expedition-route-reward-diff-ui-scales-20260810-011410` contains one standard, one large, and one small UI frame. It is a readability check, not a reliability sample.

Raw full-client PP-OCRv6 small detection on the small frame found the ten quantity positions but omitted most tiny resource labels. Cropping the reward strip and enlarging it four times before detection and recognition found all ten quantities and enough label evidence to resolve every optimization resource:

| Resource | Ground truth | Small-UI OCR evidence | Resolution |
|---|---:|---|---|
| Fuel Cell | 2x | `Zx`, `Fuel Cell` | Resource-scoped quantity normalization |
| Equipment Scrap | 12x | `12x`, `Equlpm ent`, `Scrap` | Exact quantity plus fuzzy label |
| Equipment Lock | 1x | `1x`, `Equlpm ent`, `Lock` | Exact quantity plus fuzzy label |
| Equipment Reroll | 1x | `1x`, `Equlpm ent`, `Reroll` | Exact quantity plus fuzzy label |
| Expedition Coin | 22x | `22x`, `Expedition`, `Coln` | Exact quantity plus fuzzy label |

Nine of ten quantities were exact at approximately 98.8-99.9% recognition confidence. The remaining `2x -> Zx` result was 75.3%. This supports an enlarged compact-ROI pipeline, but one frame cannot establish reliability across animated phases, random pools, or updates.

## Prototype OCR pipeline

1. Require `Route Rewards` and Back within the bounded lower Expedition route strip.
2. Capture only that compact client-relative strip.
3. Enlarge the strip four times for one batched OCR pass. Sort quantity badges by X, derive the observed card spacing, and associate split label lines only with their owning card interval.
5. Match labels against the resource vocabulary with bounded fuzzy matching and same-card spatial evidence.
6. Parse quantities using resource-scoped corrections backed by independent audited pools. The stylized `1x` result `bx` is accepted only for Equipment Lock/Reroll, where the full audit was unambiguous; `2bx/2kx` is accepted as `21x` only for Fuel Cell/Equipment Scrap. Other ambiguous forms invalidate the observation and retry instead of becoming zero or receiving a global replacement.
7. Read all five optimization resources from every pool. Require two consecutive full-pool observations with identical typed quantities before persisting a sample. A verified pool without a card for one resource records zero for that resource; a detected target card with an unreadable quantity invalidates that observation.
8. Associate labels only with the quantity in the same reward card; neighboring-card text cannot authorize a value.
9. Persist raw OCR text, normalized values, timing, selected resource, dynamic threshold, and decision in deep debug.

Full-client OCR is not the production path for these tiny labels. The expected fast path is one compact reward-strip capture and one batched OCR pass. Reroll input remains unauthorized until the route state, parsed pool, and destination transition are freshly verified.

## Optimizer contract

The runtime stores only the owner-selected target resource. There is no manual minimum. It keeps separate reward distributions for Difficulty 1, 2, and 3 and requires 500 independent pools for a usable profile; 1,000 per difficulty is the recommended collection target. Every complete pool updates every supported resource distribution.

The threshold is recomputed from the selected difficulty, a ten-minute expected run, and the rolling last 100 end-to-end reroll durations for the active OCR device. The reroll measurement includes leaving the current pool, game restart/loading, reopening Expedition Map, and completing the next stable OCR read. Faster environments can be more selective; slower environments accept lower quantities when that maximizes expected reward per hour. A positive threshold must retain at least 30 observed accepted samples, preventing unsupported tail estimates.

An accept decision requires `quantity >= threshold`. Otherwise the runner may perform one bounded restart/reroll transition, verify that a new route pool is present, and reevaluate. Valid observed pools below the configured threshold may continue rerolling until one is accepted or the user stops the Macro. Each observation, transition, and failed-read retry still needs a hard local bound so unreadable pools cannot create an unbounded blind-input loop; repeated ambiguity enters the unattended recovery ladder. The provisional transition sequence is recorded in [Expedition runtime](EXPEDITION-RUNTIME.md).

The optimizer must keep capture/OCR policy separate from reward economics. OCR reports a typed pool observation; a pure policy consumes observations and timing estimates to decide accept or reroll.

## Runtime Lab trial tester

Runtime Lab exposes an owner-triggered Route Optimizer Test for collecting bounded live evidence. The owner starts at verified Expedition Match Prestart, labels the current pool Difficulty 1-3, chooses one display target, and requests 1-1000 trials. Every trial reads the production compact reward strip at four-times scale, persists all five target quantities after two matching observations, and shows LEARNING until that difficulty reaches 500 pools. It then displays the dynamic threshold and ACCEPT/REROLL decision. The REROLL column is the measured end-to-end time from the preceding pool to the current stable pool.

The first verified Expedition menu supplies an operation-scoped Map point. Between trials the tester retries Back while Back remains visible, Start Game while Match Prestart remains visible, and Restart while Restart plus Cancel remain visible. Once confirmation disappears, it clicks the cached Map point until exact Back evidence proves the map is open. One unreadable reward observation is recorded as ERROR and recovered through the observed Back state; it does not discard an otherwise useful long sampling batch. Stop, page navigation, unverified transition recovery, or Runtime Lab shutdown still cancels without speculative input.

The shared persistent OCR channel retries transient Windows request-file access denial in both the worker and managed caller. This applies Macro-wide; semantic OCR failures are not classified as access races and retain normal recovery behavior. When deep debug is enabled, the complete operation retains each trial observation and transition.

## Validation still required

When the game is available again:

1. Capture at least 500 independent pools per difficulty; 1,000 per difficulty is recommended. Compare successive 200-pool threshold estimates and extend to 1,500-2,000 if the selected threshold remains unstable.
2. Add equivalent standard and large UI coverage after the update; transformed copies of one frame do not count as independent evidence.
3. Record manual ground truth for resource, quantity, and card position.
4. Measure card-detection recall, exact quantity accuracy, normalized quantity accuracy, false resource matches, and whole-pool acceptance accuracy.
5. Include negative states: no reward strip, partially obscured strip, transition frames, duplicate-looking labels, and stale pre-update artwork.
6. Re-estimate reroll and run durations, recompute thresholds, and compare expected throughput with a no-reroll baseline.
7. Owner-test bounded accept, reroll, failed-read, cancellation, and update-changed-layout paths before enabling unattended use.

Until that validation passes, reward-pool OCR and reroll optimization remain a **Prototype** rather than a supported release feature.
