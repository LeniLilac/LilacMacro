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

On the first semantic calibration after match start:

1. locate the live progress-bar structure in a bounded top-center search region;
2. derive the current marker from the live filled-bar endpoint and marker geometry without requiring a particular hue;
3. sweep left-to-right across the dataset-owned `Hover Line`, stopping only when the dataset-owned multi-scale tooltip-title band exposes one known node title;
4. retain the measured marker-to-hover offset for later current markers; if that cached point no longer exposes a title, perform a bounded local horizontal reacquisition around the freshly located marker;
5. accept only a known title: Defense, Elite, Assault, Boss, Encounter, or Checkpoint;
6. sample the current-node color from the freshly located bar in the same observation and update that local environment's personalized profile.

The `Hover Line` (`300,73,746,3`) and tooltip-title band (`348,61,660,55`) come from `expedition-node-set4-20260812-204347` and cover all three recorded UI scales. The tooltip geometry establishes that the cursor is over a real node. OCR supplies the semantic label. OCR alone does not authorize unrelated input.

Unknown is relevant only while a marker is in the future. When that marker becomes current, wait for its revealed live type and calibrate that type; do not classify current Unknown as Boss or any other node.

### Color hot path

After OCR has labeled a current node, store a small distribution from multiple fresh bar samples rather than one RGB value. A later observation may use color as the fast path only when:

- the progress bar and current sample region are freshly relocated;
- the environment and layout fingerprint match the profile;
- the sample is sufficiently close to exactly one learned node profile with a safe margin from the runner-up; and
- the same classification remains stable across consecutive captures.

Missing, stale, weak, or ambiguous color evidence falls back to the cached marker-relative hover point plus OCR, then bounded local reacquisition when needed. A successful fallback refreshes both the hover offset and learned color profile. Color never guesses a node and never outranks a contradictory verified tooltip.

### Cache scope

Color profiles are local calibration, not shared Macro configuration. Key them by the managed instance or desktop identity, Windows user/session target, capture/HDR environment signature, and rendered bar-layout fingerprint. Shared plans and settings must not merge Runner 1, Runner 2, RDP, and owner-desktop color profiles.

Revalidate the cache after Roblox relaunch and invalidate the affected entry after UI-scale normalization, client/layout disagreement, capture-path or HDR change, repeated color ambiguity, or a verified OCR contradiction. Geometry may be relearned without deleting otherwise valid node samples when only the layout changed.

The older ExpeditionsMacro implementation supports color as an efficient signal: it adaptively maps a small bar ROI, uses the median hue of sufficiently saturated pixels, rejects distant or ambiguous prototypes, and requires temporal stability. LilacMacro may reuse that design intent, but it must learn its own per-environment profiles and thresholds from OCR-verified LilacMacro observations rather than copy the old global prototypes or fixed coordinates.

## Route reward reroll flow

The implemented owner-observed route optimizer flow is:

1. after the Match Preview Start action, wait through the lobby-to-match teleport until the live in-match Start Game prompt is freshly visible, then align the camera;
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

Live optimization requires the selected reward quantity to be stable across fresh observations. An unrelated unreadable reward no longer invalidates a reliable selected reward, and an incomplete row is not added to the learned full-pool distribution. If the selected reward itself cannot be read, the runner closes the route page and uses the existing in-match restart flow to request another pool instead of escalating directly to a private-server rejoin.

## Node behavior

| Node | Intended behavior |
|---|---|
| Defense | First wait for fresh visible Start Game evidence; then replay every active placement; units with no selection panel are retained physical units, while a replacement phantom receives its saved Target and Auto Upgrade configuration; then reacquire and click Start Game |
| Elite | Same replay/configuration policy as Defense |
| Assault | Do not place; wait for node completion |
| Boss | Do not place; wait for node completion and count the stable real Boss transition |
| Encounter | Wait for ship arrival, then use the verified Continue source and confirmation states |
| Checkpoint | At spawn, continue immediately after setup; later checkpoints wait for ship arrival before applying the extraction policy or continuing |

The spawn node is always a Checkpoint. After the route is accepted and Start Game is clicked, the runner executes every authored placement/configuration step as one Expedition phase, then locates the spawn-specific single Continue source and clicks it before confirming in the separately owned modal. It does not require the later-checkpoint Extract/Continue pair. Each Expedition unit is expected to have one active placement.

Defense and Elite node-color/tooltip evidence may become current while the ship is still traveling. That node evidence cannot authorize placement replay. The runner waits at the node-action boundary until the live Start Game prompt is freshly visible, then replays/reconfigures units. After replay it retries fresh Start Game acquisition long enough for the field-observed Enemies Incoming overlay to clear. Only a verified Start Game click completes the boundary; node probing cannot resume while the prompt remains visible.

The Match Preview Start acknowledgment does not prove that the destination process or scene has loaded. Expedition runtime therefore waits for fresh, visible Start Game evidence for up to two minutes before any camera, route, or placement input. Story, Raid, Event, and Challenge use the ordinary Match Preview-to-Match Prestart transition with the same destination evidence; Repeat Stage re-verifies Match Prestart before resuming runtime.

## Encounter flow

Encounter node classification may become stable before the ship reaches its stop point. Classification selects the Encounter workflow but does not authorize input. The runner observes for up to two minutes until the dataset-owned source Continue control or its destination confirmation modal is freshly verified, then performs the same destination-first Continue transition used by checkpoints. The source control and confirmation modal own separate regions; the background source Continue cannot impersonate the modal action.

The first spawn Checkpoint remains immediate because setup begins there. A later Checkpoint uses the same bounded arrival wait as Encounter before Continue or Extract. The 500-frame, 60-second `expedition-spawn-node-encounter-20260814-083006` sequence is timing evidence for this separation and is not bundled wholesale as a runtime search dataset.

## Checkpoints and completion

A Boss is counted as real only retrospectively when its next stable node is Checkpoint. Checkpoints before a Boss are continued and do not increment the count. When the configured real-Boss count has been reached, the runner clicks Extract and its confirmation; otherwise it clicks Continue and its confirmation. Victory and Defeat remain terminal owners. The random level-up reward card is intentionally not OCR-gated: while no node action owns input, the runner clicks the annotated card center once per second between fresh terminal and node observations.

## Evidence still required

Before promoting this Prototype to Implemented, capture and validate:

- each current node at standard, small, and large UI scale, including no-tooltip, between-marker, transition, obscured-bar, and unrelated-top-bar negatives;
- repeated OCR-labeled examples per node and environment to measure personalized color drift, separation, and fallback frequency;
- additional independent Route Rewards pools at every scale and negative/transition states;
- owner live Encounter and non-spawn Checkpoint runs covering delayed ship arrival, source Continue, confirmation, and return to node monitoring;
- owner live runs covering checkpoint extraction and retained-versus-replaced placements after Defense/Elite.
