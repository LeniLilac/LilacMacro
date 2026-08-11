# Expedition reward optimization

**Status: Prototype field evidence and Planned runtime behavior.** The game is currently unavailable for additional capture, so every threshold below remains provisional until the owner supplies broader post-update evidence.

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

The 414-frame sample produced these provisional accept-at-or-above thresholds:

| Optimization target | Provisional minimum |
|---|---:|
| Fuel Cell | 34 |
| Equipment Scrap | 35 |
| Equipment Reroll | 4 |
| Equipment Lock | 5 |
| Expedition Coin | 34 |

These values describe this sample and the timing assumptions above. They are not constants to embed in workflow code. Recompute them when the reward distribution, route duration, restart duration, extraction strategy, or game update changes.

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

## Planned OCR pipeline

1. Detect a unique `Route Rewards` anchor in a bounded Expedition route ROI.
2. Derive the reward-strip geometry from the live anchor and current client/UI scale; do not persist desktop coordinates or one scale's fixed pixels.
3. Capture only the compact reward strip through the bounded GPU-region path.
4. Enlarge the strip for detection, segment cards from their repeated horizontal geometry, and associate quantity and label lines by X overlap.
5. Match labels against the resource vocabulary with bounded fuzzy matching and same-card spatial evidence.
6. Parse quantities using the resource's observed support and OCR confidence. Ambiguous glyphs such as `Zx` must never have one global replacement: the same OCR shape may represent different values for different resource distributions.
7. Require one unambiguous resource and one plausible quantity in the same card. Duplicate, missing, or conflicting cards fail closed and request fresh evidence.
8. Persist raw OCR text, normalized value, confidence, card bounds, timing, selected resource, threshold, and accept/reroll decision in deep debug.

Full-client OCR is not the production path for these tiny labels. The expected fast path is one compact reward-strip capture and one batched OCR pass. Reroll input remains unauthorized until the route state, parsed pool, and destination transition are freshly verified.

## Optimizer contract

The runtime should store an owner-selected target resource and either:

- an automatically recomputed threshold from accepted local evidence; or
- an explicit owner override.

An accept decision requires `quantity >= threshold`. Otherwise the runner may perform one bounded restart/reroll transition, verify that a new route pool is present, and reevaluate. The eventual implementation needs a hard elapsed-time or attempt bound so unreadable pools cannot create an infinite reroll loop.

The optimizer must keep capture/OCR policy separate from reward economics. OCR reports a typed pool observation; a pure policy consumes observations and timing estimates to decide accept or reroll.

## Validation still required

When the game is available again:

1. Capture at least 30-50 independent small-UI pools spanning different quantities, card mixes, and animation phases.
2. Add equivalent standard and large UI coverage after the update; transformed copies of one frame do not count as independent evidence.
3. Record manual ground truth for resource, quantity, and card position.
4. Measure card-detection recall, exact quantity accuracy, normalized quantity accuracy, false resource matches, and whole-pool acceptance accuracy.
5. Include negative states: no reward strip, partially obscured strip, transition frames, duplicate-looking labels, and stale pre-update artwork.
6. Re-estimate reroll and run durations, recompute thresholds, and compare expected throughput with a no-reroll baseline.
7. Owner-test bounded accept, reroll, failed-read, cancellation, and update-changed-layout paths before enabling unattended use.

Until that validation passes, reward-pool OCR and reroll optimization remain **Planned** rather than part of the Expedition runner.
