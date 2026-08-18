# Placement authoring

**Status: Prototype.** Setup can discover maps, edit per-route timelines, autosave them, and owner-test the active route from Match Prestart. Runtime Lab can execute a selected Story/Raid/Challenge route with bounded evidence; unattended main-macro playback remains Planned.

## Map discovery

Setup scans finalized manifests under `Documents\LilacMacro Datasets`, groups them by exact dataset name, and uses the newest matching dataset for each configured reference. Images are read in place and are never copied into source or build output.

Current catalog entries are:

| Mode | Maps and reference behavior |
|---|---|
| Story | School Grounds, Flower Forest, Rose Kingdom, Fairy King Forest, King's Tomb, and East Town; King's Tomb combines two compatible reference datasets |
| Raid | Spirit City Act 1, Act 2, and Act 3 as separate cards |
| Expedition | School Grounds, Flower Forest, Rose Kingdom, and East Town |
| Events | Gallery category exists, but no map definitions are implemented |

Only datasets with the same client dimensions as the primary reference are combined. Every compatible frame becomes a selectable view. Missing or invalid datasets produce no card rather than guessed imagery.

## Route model

- Story exposes Shared, Act 1-5, Infinite, Mastery, and Challenge.
- Raid exposes Shared plus the act represented by that map card.
- Expedition uses one Default route, stored as the shared route.
- An exact Story or Raid route reads Shared until its first committed edit automatically clones Shared with new stable step IDs.
- `Reset` removes an exact-route override and returns that route to live inheritance.
- `Reset` on Shared removes every authored action and retains the required `Start Game` boundary.

The compact route selector remains in the map header. It is not a consecutive execution list.

## Route defaults

Each route stores:

- team slot 1-8;
- selected unit slot 1-6;
- between-upgrade settle delay 0-60000 ms, default 200 ms;
- targeting priority;
- Auto Upgrade priority.

While the Setup placement workspace owns keyboard focus, number-row or numpad keys `1` through `6` select the matching Unit Slot. The shortcut uses the same autosaved route-default path as the visible slot buttons and is suppressed while a text field, combo box, or owner dialog owns input.

Defaults initialize new timeline actions; existing actions retain their saved values until edited. Team is a stored header control between route configuration and reference view. The stored floating placement palette defaults to the workarea's top-left corner, can be dragged by its header within that workarea, and contains explicit `Place` and `Select` cursor modes plus Unit Slot 1-6. Cursor mode is session-only and changes only through the mode controls; placing a unit and using a Unit Slot shortcut never switch it. Palette movement is also session-only and never changes placement coordinates. The adjacent Match Settings view fills the available width and mirrors default targeting, default Auto Upgrade, between-unit-check delay, placement attempts, and impossibility threshold from ExpeditionsMacro. Its Advanced Settings dropdown reveals the copied Step Mode delays, placement/reconfigure proof checks, upgrade-readiness check, prestart check, and recording playback delay. `Between upgrades, ms` is implemented, autosaved per route, and controls the settle before the next Upgrade readiness check; the other Match Settings controls remain **Prototype UI only** and do not modify the document.

`Copy Setup` replaces the active route with a selected source map and route in one validated autosaved edit. It copies the saved team, selected Unit Slot, route defaults, between-upgrade settle, placements, and all other timeline actions with new stable IDs and remapped dependent references. Placement coordinates scale proportionally when source and target image dimensions differ. Views of the same map already share one setup, and the remaining unwired Match Settings prototype controls are not persisted source state.

## Timeline

Every route contains exactly one movable `Start Game` boundary. Actions above it are authored for prestart; actions below it are authored for after start.

Prestart and after-start Delay actions use the same 1-3600000 ms duration bound. There is no implicit pause after a timeline step. The retired game auto-start timer no longer imposes a separate aggregate prestart budget. Evidence waits whose duration depends on live game state remain independently bounded by their runtime policy.

In `Place` mode, a left-click on the map immediately creates a Place action at the original-image pixel coordinate using the selected Unit Slot and route defaults. Every marker inside the pointer's fixed viewport-space proximity radius hides its label and dims its pin together; markers cannot be dragged or deleted. In `Select` mode, empty-map and marker-body clicks do nothing and never select or scroll the timeline. Hovering near a marker raises it above neighboring markers and turns the complete label into a red delete button. Dragging its exact placement dot previews a new point before atomically saving the final coordinate, while nearby non-dragged markers dim with the same fixed viewport-space proximity rule used during placement. There is no separate delete icon and the label never claims drag ownership. Select mode is never entered automatically after placement.

Each point is rendered as one compact pin with its unit-slot label integrated directly above the exact saved point; long collision-routing connectors are not shown. The complete marker counter-scales against map zoom around its saved point, retaining one constant screen-space size so zooming in exposes spacing between crowded labels. A slot used once is labeled `1`; repeated placements of that slot are labeled `1a`, `1b`, and so on in timeline order. Moving retains the stable placement ID, so dependent Reconfigure, Upgrade, and Sell actions continue to reference the moved point. Deleting uses the existing cascade rule for dependent unit actions. Invalid drops fail validation and restore the saved position.

`Add Step` sits between `Test Setup` and `Popout` and opens one centered owner-modal editor for Delay, Reconfigure, Upgrade, and Sell plus the fields for the selected action; Place is not offered there. Reconfigure, Upgrade, and Sell select an earlier placement by the same compact `1`, `1a`, `1b` label shown on the map, without a repeated unit description. `Edit` opens the same styled field surface for that row. The complete row is a reorder handle: once dragged, a translucent row preview follows the pointer, the source leaves a stable gap, and a pink insertion line tracks every valid boundary before the first row through after the last row. Releasing anywhere in the captured timeline commits that boundary, including moving Start Game above or below placement rows; pausing near a visible edge scrolls the owning list or page. No separate up/down controls are exposed. The step kind is shown by a colored action rail and title, with separate light- and dark-palette colors. Position around the Start Game boundary conveys before/after state, so phase chips are not shown. Selection uses a themed surface and ink outline rather than native ListBox selection chrome.

| Action | Stored contract |
|---|---|
| Place | Stable ID, unit slot, original-image `x/y`, targeting, and Auto Upgrade priority |
| Delay | Duration of 1-3600000 ms |
| Reconfigure | Earlier placement ID and at least one targeting or Auto Upgrade change |
| Upgrade | Earlier placement ID and count 1-100 |
| Sell | Earlier placement ID |
| Start Game | Required boundary; cannot be deleted or replaced |

Reconfigure, Upgrade, and Sell may reference only an earlier Place step. Deleting a Place step also deletes all of its dependent unit-action steps. Placement IDs remain stable within the route. Different placement points must be at least 7 image pixels apart.

The timeline is normal page content directly below the map. The docked map and timeline share one vertical workspace scroller; there is no fixed-height dock row, divider, or nested timeline scrollbar. Match Steps and Match Settings switch through a slim underline tab bar. `Test Setup` flushes autosave, resizes Roblox to the canonical `1366 x 700` client when needed, applies the standard camera alignment, and runs the complete active timeline through Start Game without navigation or terminal waiting. While it owns input, the button becomes `Stop Test`, the configured Macro start/stop key also cancels the test, and route, map, and timeline edits are locked. `Popout` reparents the same live editor into one themed, borderless, resizable window whose title bar contains only window controls; its bounded list and settings view regain their own thin scrollbars. `Dock` or closing the window returns it to the page without duplicating state or losing selection.

## Coordinates and reference views

Map clicks and placement-dot drags are transformed into original-image pixel coordinates, not viewport, desktop, or current zoom coordinates. Persisted points must remain within the saved image dimensions. The map workspace keeps a constant height while its image zoom changes; zooming out exposes more themed card surround instead of shrinking the workspace. Mouse-wheel zoom stays anchored to the pointer only when the active wheel burst began over the map; a burst that began elsewhere continues ordinary page scrolling even if the pointer crosses into the map. Middle-button drag pans the image even while it fits completely within the workarea. Initial load or viewport resize fits and centers the complete image without changing saved coordinates. No permanent Fit control is exposed. Switching reference views does not transform placements; only dimension-compatible views are admitted for one map reference.

## Autosave

Committed edits are validated, cloned into a snapshot, serialized through one ordered save queue, and atomically replaced under the active configuration root's `placements\<map-id>.json`. That root is owner-local before instance setup, shared for This desktop and shared Runners, or profile-specific for a separate Runner. A newer valid edit still receives a save attempt if an earlier write failed. Opening another map and closing the macro shell flush pending saves; recoverable failures use the macro shell's dismissible bottom-right error toast instead of hidden inline text.

Placement documents use schema version 1 and store the map ID, image dimensions, Shared route, and route overrides. Personal map images remain in their dataset directories.

## Playback boundary

**Prototype:** Setup, Runtime Lab, and the main Macro map authored image points to the verified 1366 by 700 client, batch contiguous Place steps through Quick placement, retry only failed rows, verify configurable phantom or physical panels, execute the Start Game boundary, and support Delay, Reconfigure, Upgrade, and Sell. Quick-placement input uses the ExpeditionsMacro-proven 110 ms unit-key hold, 250 ms selection settle, 75 ms cursor settle, and three-click 50 ms burst. Targeting, Auto Upgrade priority, Reconfigure, and Sell may act on a stable `DPS ???` phantom; the selected-panel Priority/Sell image proof uses the stored `DPS ???` reference as a conservative phantom fast path, then DPS OCR classifies every non-`???` state as physical, including a numeric read where OCR omitted the trailing `/s`. Every requested Upgrade press receives a fresh affordability check; presses after the first wait the route's between-upgrade settle, default 200 ms, before that check. No completed step adds an implicit delay. A selection-proof miss receives at most three fresh reselection attempts; after exhaustion, that authored step is logged as skipped and playback advances without restarting Roblox. If the selected-unit panel cannot be dismissed safely, or another structural/cancellation failure occurs, the owning recovery path still handles it. After Place configuration, Reconfigure, or Upgrade, the shared playback service closes the selected-unit panel through bounded bottom-right resting-point clicks and requires fresh hidden-panel proof before advancing; Sell proves closure itself. Setup's owner test begins at Match Prestart, performs camera alignment first, and returns after the authored after-start actions. Runtime Lab and the main Macro may continue to terminal verification. All three stop on ambiguity or cancellation. Owner live acceptance remains required.

Playback finishes every authored prestart action in order. At the `Start Game` boundary it requires the normal fresh Start-screen action before continuing with authored after-start actions. A missing Start screen remains indeterminate and is freshly reobserved/retried for up to two minutes before failing closed; physical selected-unit evidence no longer substitutes for this transition.

The Setup test does not:

- navigate to the selected map or verify that its route matches the open game;
- load the authored team;
- wait for Victory or Defeat, repeat the stage, or return to Lobby;
- integrate with the priority scheduler.

Do not copy ExpeditionsMacro detection heuristics into LilacMacro. Only the authoring feature set and data intent were ported; runtime detection will be designed against LilacMacro's own evidence model.
