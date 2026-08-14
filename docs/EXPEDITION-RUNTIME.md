# Expedition runtime

**Status: Prototype.** The main Macro, Runtime Lab Wire flow, and managed-session runtime now share the bounded Expedition runner described here. Owner live acceptance and broader negative-state coverage remain required.

## Current-node evidence

Dataset names below are relative to `Documents\LilacMacro Datasets` and remain private local evidence.

| Dataset | Evidence |
|---|---|
| `expedition-node-set-1-20260812-191008` | 24 standard-scale frames spanning live node progress and hover cards, including Defense, Boss, and Checkpoint |
| `expedition-node-set-2-20260812-193051` | 7 standard-scale frames including Assault and Encounter hover cards |
| `expedition-node-set-3-20260812-193424` | 10 standard-scale frames including an Unknown future marker and its later revealed Assault state |
| `expedition-node-set4-20260812-204347` | 6 no-hover/hover frames at three Roblox UI scales within the canonical `1366 x 700` client |

The multi-scale set shows that the node bar remains in a narrow top-center band while its span, marker spacing, tooltip size, and tooltip position change with Roblox UI scale. Raw coordinates and one global set of node colors are not accepted runtime owners.

## Personalized current-node detector

Only the current node is required. Future-node lookahead is not part of the Macro design.

### Semantic authority

On a cache miss or failed fast-path classification:

1. locate the live progress-bar structure in a bounded top-center search region;
2. derive the current marker from the live filled-bar endpoint and marker geometry without requiring a particular hue;
3. hover the current marker, wait for a structurally valid tooltip across fresh captures, and OCR its title;
4. accept only a known title: Defense, Elite, Assault, Boss, Encounter, or Checkpoint;
5. sample the current-node color from the freshly located bar in the same observation and update that local environment's personalized profile.

The tooltip geometry establishes that the cursor is over a real node. OCR supplies the semantic label. OCR alone does not authorize unrelated input.

Unknown is relevant only while a marker is in the future. When that marker becomes current, wait for its revealed live type and calibrate that type; do not classify current Unknown as Boss or any other node.

### Color hot path

After OCR has labeled a current node, store a small distribution from multiple fresh bar samples rather than one RGB value. A later observation may use color as the fast path only when:

- the progress bar and current sample region are freshly relocated;
- the environment and layout fingerprint match the profile;
- the sample is sufficiently close to exactly one learned node profile with a safe margin from the runner-up; and
- the same classification remains stable across consecutive captures.

Missing, stale, weak, or ambiguous color evidence falls back to current-marker hover plus OCR. A successful fallback refreshes the learned profile. Color never guesses a node and never outranks a contradictory verified tooltip.

### Cache scope

Color profiles are local calibration, not shared Macro configuration. Key them by the managed instance or desktop identity, Windows user/session target, capture/HDR environment signature, and rendered bar-layout fingerprint. Shared plans and settings must not merge Runner 1, Runner 2, RDP, and owner-desktop color profiles.

Revalidate the cache after Roblox relaunch and invalidate the affected entry after UI-scale normalization, client/layout disagreement, capture-path or HDR change, repeated color ambiguity, or a verified OCR contradiction. Geometry may be relearned without deleting otherwise valid node samples when only the layout changed.

The older ExpeditionsMacro implementation supports color as an efficient signal: it adaptively maps a small bar ROI, uses the median hue of sufficiently saturated pixels, rejects distant or ambiguous prototypes, and requires temporal stability. LilacMacro may reuse that design intent, but it must learn its own per-environment profiles and thresholds from OCR-verified LilacMacro observations rather than copy the old global prototypes or fixed coordinates.

## Route reward reroll flow

The implemented owner-observed route optimizer flow is:

1. load the Expedition match and align the camera;
2. open Expedition Map and parse Route Rewards;
3. click Back;
4. if the pool satisfies the configured optimization requirement, continue into the Expedition;
5. otherwise click Start Game only when fresh Start evidence is present;
6. open Settings, verify and click the yellow Restart Game action, then verify and click its confirmation;
7. wait for the new route state, reopen Expedition Map, parse the new reward pool, and click Back;
8. repeat from step 4 until a valid observed pool satisfies the requirement or the user stops the Macro.

Valid rejected pools may continue rerolling. Every individual observation, transition, and failed-read retry remains bounded. Repeated unreadable or indeterminate states use the unattended recovery ladder rather than becoming an unbounded blind click loop.

Restart is available only after Start Game. Before the game has started, the same Settings slot is Teleport to Lobby; the runner therefore verifies Start before opening Settings for a rejected route. The Settings panel action points are derived from its detected bounds, the Restart confirmation requires both Restart and Cancel evidence, and a successful confirmation must return to verified Expedition prestart.

Route reward OCR uses the compact lower strip enlarged four times. It associates resource labels and quantities within the same repeated card column. The field-observed `Zx` correction is scoped only to Fuel Cell; it is not a global glyph replacement. Accepting a route continues through Start Game when present, initial placement, Continue, and then current-node execution.

## Node behavior

| Node | Intended behavior |
|---|---|
| Defense | Replay every active placement; units with no selection panel are retained physical units, while a replacement phantom receives its saved Target and Auto Upgrade configuration; then Start Game when present |
| Elite | Same replay/configuration policy as Defense |
| Assault | Do not place; wait for node completion |
| Boss | Do not place; wait for node completion and count the stable real Boss transition |
| Encounter | Run the map-specific encounter workflow below |
| Checkpoint | Apply the configured extraction policy or continue |

The spawn node is always a Checkpoint. After the route is accepted and Start Game is clicked, the runner executes every authored placement/configuration step as one Expedition phase, then clicks Continue and its confirmation. Each Expedition unit is expected to have one active placement.

## Encounter flow

After Encounter is stably current, wait 15 seconds for travel. Then:

1. open verified Settings;
2. click verified Teleport to Spawn;
3. close Settings and revalidate the game;
4. perform the map-specific movement;
5. press `E` and verify the interaction UI, for at most three attempts;
6. if all three attempts fail, record a defeat for the attempt and use the verified Settings restart flow before the scheduler continues;
7. choose the intended conversation action, use bounded clicks on the live dialogue box to advance dialogue, verify and click Yes, then continue bounded dialogue advancement;
8. verify that dialogue ended and the player returned to the ship before resuming node monitoring.

| Map | Forward | Right |
|---|---:|---:|
| School Grounds | `W 350 ms` | `D 700 ms` |
| Flower Forest | `W 350 ms` | `D 700 ms` |
| Rose Kingdom | `W 1000 ms` | `D 700 ms` |
| East Town | `W 700 ms` | `D 700 ms` |

Two menu vocabularies are supported: Discuss/Barter/Engage/Leave and Speak/Barter/Engage/Leave. Speak and Discuss are the leftmost equivalent action; some NPCs expose both. The runner verifies that left action plus at least two supporting menu actions, then alternates bounded clicks between that live left-action point and the field-observed dialogue/Yes area.

## Checkpoints and completion

A Boss is counted as real only retrospectively when its next stable node is Checkpoint. Checkpoints before a Boss are continued and do not increment the count. When the configured real-Boss count has been reached, the runner clicks Extract and its confirmation; otherwise it clicks Continue and its confirmation. Victory and Defeat remain terminal owners. The random level-up reward card is intentionally not OCR-gated: while no node action owns input, the runner clicks the annotated card center once per second between fresh terminal and node observations.

## Evidence still required

Before promoting this Prototype to Implemented, capture and validate:

- each current node at standard, small, and large UI scale, including no-tooltip, between-marker, transition, obscured-bar, and unrelated-top-bar negatives;
- repeated OCR-labeled examples per node and environment to measure personalized color drift, separation, and fallback frequency;
- additional independent Route Rewards pools at every scale and negative/transition states;
- Encounter interaction prompts, both menu variants, dialogue, Yes, completion, and three-attempt failure states for every map;
- owner live runs covering checkpoint extraction and retained-versus-replaced placements after Defense/Elite.