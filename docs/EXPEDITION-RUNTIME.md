# Expedition runtime

**Status: Prototype.** The main Macro, Runtime Lab Wire flow, and managed-session runtime now share the bounded Expedition runner described here. Owner live acceptance and broader negative-state coverage remain required.

## Current-node evidence

The compact runtime evidence slices below are bundled under `src/LilacMacro.App/Assets/RuntimeEvidence`; the original full datasets remain in `Documents\LilacMacro Datasets`.

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
5. move the pointer without clicking to the established bottom-right resting point and allow the hover card to clear before handing the node action to another state owner;
6. accept only a known title: Defense, Elite, Assault, Boss, Encounter, or Checkpoint;
7. sample the current-node color from the freshly located bar in the same observation and update that local environment's personalized profile.

The color sample is limited to the seven-pixel fill stripe centered on the located marker. Saturated map scenery above the stripe is excluded; it previously could dominate a median and teach a semantic node the background hue instead of the bar hue. Color-profile version 2 invalidates those earlier samples.

The `Hover Line` (`300,73,746,3`) and tooltip-title band (`348,61,660,55`) come from `expedition-node-set4-20260812-204347` and cover all three recorded UI scales. The tooltip geometry establishes that the cursor is over a real node. OCR supplies the semantic label. OCR alone does not authorize unrelated input.

Unknown is relevant only while a marker is in the future. When that marker becomes current, wait for its revealed live type and calibrate that type; do not classify current Unknown as Boss or any other node.

### Color hot path

After OCR has labeled a current node, store a small distribution from multiple fresh bar samples rather than one RGB value. Color may retain the fast path only while the freshly relocated marker is the same marker that received the semantic label. Movement to a new marker always reacquires its tooltip label before changing passive run state. This is stricter than nearest-profile classification because a complete profile was observed accepting incorrect long-run node transitions and delaying extraction for more than an hour.

A retained-marker observation may use color only when:

- the progress bar and current sample region are freshly relocated;
- the environment and layout fingerprint match the profile;
- the sample remains close to the hue recorded with that marker's semantic observation; and
- the same classification remains stable across consecutive captures.

Movement to a new marker returns to hover/OCR even when every node type has already been calibrated. A complete personalized profile remains diagnostic/calibration data; it does not authorize a cross-marker state transition.

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
| Defense | First wait for fresh visible Start Game evidence; then replay placements not already retained as physical. The game moves physical units into the node area and deletes phantoms: no selected-unit panel at the saved point means the existing physical was moved and is skipped on later Defense/Elite nodes, while any selected-unit panel means replay placement succeeded as either phantom or affordable physical, receives its saved Target and Auto Upgrade configuration, and remains eligible for later replay. Replay does not classify DPS; then reacquire and click Start Game |
| Elite | Same per-placement retention/replay policy as Defense |
| Assault | Do not place; wait for node completion |
| Boss | Do not place; wait for node completion and count the stable real Boss transition |
| Encounter | Wait for ship arrival, then use the verified Continue source and confirmation states |
| Checkpoint | At spawn, continue immediately after setup; later checkpoints wait for ship arrival before applying the extraction policy or continuing |

The spawn node is always a Checkpoint. After the route is accepted and Start Game is clicked, the runner executes every authored placement/configuration step as one Expedition phase, then locates the spawn-specific single Continue source and clicks it before confirming in the separately owned modal. It does not require the later-checkpoint Extract/Continue pair. Each Expedition unit is expected to have one active placement.

Defense and Elite node-color/tooltip evidence may become current while the ship is still traveling. That node evidence cannot authorize placement replay. The runner waits at the node-action boundary until the live Start Game prompt is freshly visible, then replays/reconfigures units. After replay it retries fresh Start Game acquisition for up to two minutes so the field-observed Enemies Incoming overlay can clear. Only a verified Start Game click completes the boundary; node probing cannot resume while the prompt remains visible.

Node type and node arrival are separate temporal identities. Consecutive Defense nodes or consecutive Elite nodes may expose the same semantic tooltip title, so equality with the previous node type cannot suppress the next action. A newly visible, verified Start Game episode reopens Defense/Elite replay even when the type is unchanged. The episode remains latched after the verified click and may reopen only after fresh evidence observes the prompt absent, preventing duplicate replay on one node.

The Match Preview Start acknowledgment does not prove that the destination process or scene has loaded. Expedition runtime therefore waits for fresh, visible Start Game evidence for up to two minutes before any camera, route, or placement input. Story, Raid, Event, and Challenge use the ordinary Match Preview-to-Match Prestart transition with the same destination evidence; Repeat Stage re-verifies Match Prestart before resuming runtime.

When priority reevaluation selects the exact same Expedition task after Victory or Defeat, the scheduler uses the freshly verified Repeat Stage action instead of returning to Lobby. The repeated runtime waits for a new visible Start Game boundary, reruns route optimization and new-match placement setup, and retains the already-loaded team and camera. Challenge and future Tower tasks remain one-match continuations and reset through Lobby.

## Encounter flow

Encounter node classification may become stable before the ship reaches its stop point. Classification selects the Encounter workflow but does not authorize input. The runner observes for up to two minutes until the dataset-owned source Continue control or its destination confirmation modal is freshly verified, then performs the same destination-first Continue transition used by checkpoints. The source control and confirmation modal own separate regions; the background source Continue cannot impersonate the modal action.

The first spawn Checkpoint remains immediate because setup begins there. A later Checkpoint uses the same bounded arrival wait as Encounter before Continue or Extract. The 500-frame, 60-second `expedition-spawn-node-encounter-20260814-083006` sequence is timing evidence for this separation and is not bundled wholesale as a runtime search dataset.

The node detector is not an input owner for Encounter or Checkpoint. Each runtime cycle observes terminal states first, then periodically probes the separately dataset-owned later-Checkpoint source first and Encounter source second, before passive node classification. A Checkpoint action requires the independent Extract-plus-Continue pair inside its bounded source ROI; an Encounter action requires its own source Continue ROI. The passive color/tooltip result may describe a pending workflow, but it cannot click Continue or Extract or enter the terminal wait. Repeated observation of the same Checkpoint source replays the last Checkpoint action rather than degrading to `Wait`. A confirmation modal is handled only by the transition that opened it; the identical Continue confirmation artwork cannot retrospectively choose between Encounter and Checkpoint.

Once a live source owns a transition, its destination/modal is observed first. A visible confirmation authorizes only its modal action. If the destination is absent and the initiating source remains stable, the source is retried within the action cap. Two fresh observations with both layers absent complete an initiated Continue transition. Only a verified Extract source-to-confirmation-to-clear transition may enter the Victory/Defeat wait. Defense and Elite remain passive descriptions until fresh Start Game evidence owns replay; Assault and Boss are the only tooltip/color results that directly update passive run history.

The local node monitor cannot wait indefinitely. Its five-minute progress watchdog resets only after a newly OCR-labeled marker, a completed Checkpoint/Encounter transition, or a completed Defense/Elite Start Game episode. Repeated color observations, repeated OCR of the same marker, and reward-popup handling do not replace node progress. Expiry returns a retryable runtime failure to the unattended scheduler recovery ladder; manual Stop remains ordinary cancellation and is not diagnosed as a stall.

## Checkpoints and completion

A Boss is counted as real only retrospectively when its next stable node is Checkpoint. Checkpoints before a Boss are continued and do not increment the count. When the configured real-Boss count has been reached, the runner clicks Extract and its confirmation; otherwise it clicks Continue and its confirmation. Victory and Defeat remain terminal owners. The random level-up reward popup is handled through the bundled `Expedition Reward Popup Action Strip` ROI. A fresh ROI OCR must expose at least three same-row `Select Upgrade` matches; after a two-second settle, the runtime re-OCRs the ROI and clicks the unique rightmost match using that fresh OCR bound, waits three seconds, and repeats for chained popups up to the bounded consecutive-popup limit. OCR boxes from the owner notes are examples only and never become static coordinates; fewer than three matches, separated rows, or a tied rightmost match fail closed. No blind idle click remains.

## Evidence still required

Before promoting this Prototype to Implemented, capture and validate:

- each current node at standard, small, and large UI scale, including no-tooltip, between-marker, transition, obscured-bar, and unrelated-top-bar negatives;
- repeated OCR-labeled examples per node and environment to measure personalized color drift, separation, and fallback frequency;
- additional independent Route Rewards pools at every scale and negative/transition states;
- owner live Encounter and non-spawn Checkpoint runs covering delayed ship arrival, source Continue, confirmation, and return to node monitoring;
- owner live runs covering checkpoint extraction and retained-versus-replaced placements after Defense/Elite.
