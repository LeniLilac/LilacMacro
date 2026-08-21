# LilacMacro UI Design

## Status

**Status: Prototype.** This document defines the implemented application shell plus the Macro, Plan, Setup, and Settings prototype surfaces. It does not claim that owner-validated unattended placement playback or the unattended macro is complete; see [docs/PROJECT-STATUS.md](docs/PROJECT-STATUS.md).

## Product scene

One owner uses LilacMacro on a Windows desktop beside Roblox, configuring runs and placements before leaving the macro to operate. The interface should feel direct and inspectable, with SquareClaim's bright paper-and-ink character and none of the explanatory panels found in onboarding-oriented software.

## Design principles

- Use short labels and visible state. Do not add marketing copy, walkthroughs, guide cards, or descriptive page introductions.
- Keep the application structure stable. Switching pages must not discard unfinished work; **Planned** runtime work must also continue safely while another page is visible.
- Use the Lilac theme resources as the visual source of truth: pink-tinted white or black surfaces, ink outlines, a pink primary accent, and restrained semantic colors.
- Reserve strong color for selection, status, and primary actions.
- Keep controls compact and desktop-oriented.
- Prefer one clear workspace over nested cards and repeated containers.

## Application shell

LilacMacro uses a single row of browser-style tabs across the top of the window.

```text
+---------+---------+---------+----------+-------------------------------+
|  Macro  |  Plan   |  Setup  | Settings | Window controls               |
+---------+---------+---------+----------+-------------------------------+
|                                                                           |
|                           Active page                                     |
|                                                                           |
+---------------------------------------------------------------------------+
```

### Tab behavior

- Tabs appear in this fixed order: Macro, Plan, Setup, Settings.
- Macro is selected when the application opens.
- Tabs use one wider, consistent width and remain fully contained inside the light or dark top chrome.
- Tabs sit directly in a thin browser-height title bar. Inactive tabs are borderless and separated by quiet dividers.
- Every tab has a left-aligned Lucide favicon tied to its page: run, priority plan, placement target, or settings controls.
- The active tab uses one quiet muted surface. It does not use the heavy outline or offset shadow of an action button.
- Inactive tabs remain clearly clickable without competing with the active tab.
- Tabs are navigation, not documents. They cannot be closed, duplicated, or reordered.
- Do not add a sidebar or duplicate page navigation elsewhere.
- Each page preserves its local selection and scroll position when another tab is opened.
- Empty title-bar space remains available for dragging the window.
- Do not reserve title-bar space for a large application mark or repeated product name.
- Standard minimize, maximize or restore, and close controls remain at the far right and use vector icons rather than font glyphs.

### Tab states

Each tab must have consistent default, hover, pressed, focused, and active states. The active tab uses a muted surface, inactive tabs sit directly on the title bar, and keyboard focus uses the pink accent outline.

## Pages

### Owner tool shells

The macro shell is not the developer-tool container. Three dedicated launchers reuse the same theme, window chrome, and applicable shared contracts:

- **Dataset Builder:** Capture, Review + OCR, and Datasets only. F5 and F6 retain manual and timed capture ownership.
- **Macro:** F7 is the default global start/stop key so it does not collide with Dataset Builder or Runtime Lab's F6 ownership. Settings from schema 1 or 2 migrate the former F6 default to F7; later user-selected keys are preserved.
- **Runtime Lab:** Debug, Wire Test, and bounded runtime tests only. Debug checks can select OCR or saved-image-first evidence with OCR fallback; actions still use fresh OCR bounds. It does not expose dataset capture or authoring navigation; F6 is available only to an explicitly armed Debug key chain. Team Scroll exposes requested units with each measured thumb position. Team Swap Test runs balanced random Teams 1-8 through the shared production path and exposes each trial's outcome. Route Optimizer runs an owner-selected number of independent Expedition reward-pool trials and exposes quantity, threshold decision, raw compact OCR text, and elapsed time. Deep debug retains the complete Runtime Lab operation and its title-bar pill reports `DEEP DEBUG ON` without a duration.
- **Deep Debug Viewer:** Dedicated archive inspection shell with a frame-first canvas, synchronized event rail, timestamp-aware transport, and optional input markers. It streams ZIP entries and does not initialize OCR or Roblox.

Each shell names itself in the title bar and constructs only its owned surface. None duplicates Core, Windows capture/input, OCR, vision, or runtime logic. Cross-process Roblox input attempts fail closed while another LilacMacro process holds the input lease.

### Macro

Purpose: dashboard and future macro execution.

- Full layout uses a large Roblox run surface with a right-side inspector and a content-sized run log below. Preserve the Roblox client's exact physical size; the inspector expands into remaining horizontal space and the log uses only its current content height up to a bounded scrollable viewport so larger work areas give priority to the dock surface.
- Full layout opens at `1920 x 1080` and exposes one compact left-aligned Dock/Undock action. Dock does not maximize the shell and presents the client at exactly `1366 x 700` physical pixels. While Dock remains enabled, a Roblox window reopened in the same desktop is reacquired automatically, including when the reopened client becomes foreground. Compact layout opens at `1366 x 768`, removes the dock surface and action entirely, and always minimizes the macro while a run owns the desktop. Undocking or any automatic dock release leaves Roblox undocked at its last docked position and size so the owner can reposition or resize it; closing restores the standalone window styles at that same geometry.
- Settings > Roblox assigns either `1920 x 1080` or `1366 x 768` to each runner row independently. `OPEN` creates a fixed-resolution, smart-scaled viewport for that runner. Managed runner UIs derive full/compact layout from their actual RDP desktop, so a compact runner removes Dock and forces minimize-while-running without changing another runner or **This desktop**.
- Keep plan selection and Start/Stop at the top of the page.
- The inspector exposes the current task, next eligible task, priority decision, Lobby-reset evidence, private-server readiness, terminal path, runtime, victories, and defeats. Its run-history chart plots monotonic cumulative victories and defeats rather than per-time-bucket rates.
- Until the runtime exists, Start/Stop changes preview state only. It must not imply that Roblox input, capture, scheduler, or persistence is active.
- Avoid mock victory/defeat controls, guide copy, prototype badges, or decorative metrics.

### Plan

Purpose: task and priority configuration.

- Present tasks as independent priority candidates, not a consecutive workflow.
- Match ExpeditionsMacro's compact Plan blocks: grip, global priority, mode, generated task summary, target/progress, and compact row actions. Do not expose separate task names, teams, placement-route inheritance, eligibility, or other Lilac-only metadata.
- Use the topmost ready task as the visible scheduling rule.
- Reorder task priority within the owning plan-level or loop list from the leading Lucide grip with the same captured-pointer, stable-gap, moving-preview, and insertion-line controller used by Setup timelines. Dropping a task directly on a loop appends it inside that loop. Drag selection and feedback clear on drop or cancellation; the task editor remains available for other cross-level moves. Do not show redundant up/down buttons.
- Plan row actions match Setup rows: a compact `Edit` button followed by the shared Lucide trash button.
- Keep plan creation, copy, rename, and deletion in one compact toolbar.
- Add and edit tasks through one centered modal rather than an anchored popover or permanent editor rail.
- Support the permanent Story, Raid, Challenge, and Expedition modes, limited Event acts, and Utilities in the editor.
- Support root tasks and repeat loops. Loop settings are `Forever` or a bounded repeat count; nested tasks retain global priority and can be targeted directly from the task editor. Forever loops are normalized to the plan-level tail, their grips are disabled, and plan-level tasks or finite loops can never remain below them.
- Match ExpeditionsMacro's task fields: destination, mode, route and target schedule, defeat retries, challenge types, Expedition difficulty/extraction/boss count, and Story hard mode.
- Committed plan edits, ordering, loops, names, and the selected plan autosave atomically to the versioned Macro settings contract. Application shutdown and Macro start flush queued writes. Runtime scheduler consumption remains Prototype.

### Setup

Purpose: map-specific placement setup.

Setup opens on a map gallery. The gallery is divided into these mode categories:

- Story
- Raid
- Expedition
- Events

Only the selected category's maps are shown. Each map appears as a thumbnail with its map name. Selecting a map replaces the gallery with that map's placement workspace.

Map references are discovered from finalized dataset manifests under `Documents\LilacMacro Datasets`. The application uses the dataset images in place and never copies personal captures into the repository or build output. When multiple frames exist, each frame becomes a selectable reference view.

#### Gallery layout

```text
SETUP

[ Story ] [ Raid ] [ Expedition ] [ Events ]

+--------------------+  +--------------------+  +--------------------+
|                    |  |                    |  |                    |
|   Map thumbnail    |  |   Map thumbnail    |  |   Map thumbnail    |
|                    |  |                    |  |                    |
+--------------------+  +--------------------+  +--------------------+
| School Grounds     |  | Flower Forest      |  | Rose Kingdom       |
+--------------------+  +--------------------+  +--------------------+
```

#### Gallery rules

- Mode categories behave as a compact tab or segmented-control row inside Setup.
- Remember the last selected category while the application remains open.
- Use a consistent thumbnail ratio and crop behavior across every category.
- Show the map name directly beneath or inside the lower edge of its thumbnail.
- The entire map tile is clickable.
- Selection must not depend on color alone. Use the ink outline and surface state as well.
- Loading and missing thumbnails use a fixed-size placeholder so the gallery does not shift.
- An empty category shows a terse empty state and no instructional panel.

#### Map placement workspace

Selecting a map opens the placement workspace within the Setup tab.

```text
SETUP / STORY / SCHOOL GROUNDS                         [Back to maps]

+-----------------------------------------------------------------------+
| Back   Map name                 [ Route / state ] [ Team ] [ View ] Reset |
+-----------------------------------------------------------------------+
|                                                                       |
| [ Movable Unit Slot palette ]       Map workspace                    |
|                                                                       |
+-----------------------------------------------------------------------+
| Match Steps  Match Settings                 Test Setup   Popout      |
|                                             Add step                  |
| Drag handle   Ordered step                          Edit  Delete      |
+-----------------------------------------------------------------------+
```

- Keep the selected mode and map visible in a compact breadcrumb or title line.
- Provide a clear Back to maps action that returns to the same gallery category and scroll position.
- Give the map most of the available workspace.
- The map workspace keeps one constant `760`-pixel height. Mouse wheel zooms the image around the pointer without resizing that workspace, so zooming out reveals more themed surface around the centered image. Holding the middle mouse button pans the map. Initial load and viewport resize fit and center the complete image automatically; there is no permanent Fit control. Zoomed content uses the shared thin scrollbars. The fitted surround uses the current theme surface rather than a fixed dark canvas.
- Keep route selection in one compact header dropdown. Do not use a permanent route rail.
- Stack the timeline below the map as normal page content. The map and docked timeline share one vertical workspace scroller, so scrolling continues through the complete Match Steps or Match Settings content. Do not use a fixed-height dock row, horizontal divider, or nested docked timeline scrollbar.
- Popout moves the same live editor into a separate resizable window and restores bounded internal scrolling there. Its title bar contains only window controls; it does not repeat a Match Timeline heading that is absent from the docked panel. Dock or closing the window returns it below the map without changing the selected route or losing edits.
- Story maps expose Shared, Act 1 through Act 5, Infinite, Mastery, and Challenge routes.
- Exact routes use the Shared setup until their first committed edit automatically creates an independent override. Reset removes that override and resumes Shared inheritance. Reset on Shared removes every authored action while retaining the required Start Game boundary.
- Raid map cards expose Shared plus the act represented by that card. Expedition maps use one Default setup.
- `Copy Setup` atomically replaces the active route from a selected map and route, including its saved team, authoring defaults, placements, and every timeline action. Placement coordinates scale between different source and target image dimensions. Reference views of one map already share one setup and do not need copying.
- The saved Team 1-8 selector sits in the map header between route configuration and the reference-view selector. The movable placement palette defaults to the workarea's top-left and contains explicit `Place` and `Select` cursor modes plus Unit Slot 1-6. Its drag grip shares the compact Mode label row instead of reserving an empty header band. Modes change only through those controls; placing a unit or pressing a number key never switches modes. Number-row and numpad keys `1` through `6` select the matching Unit Slot whenever Setup owns keyboard focus, except while the owner is typing or using a field selector. Match Steps contains only the action list and Add Step. Match Settings uses the full panel width for the ExpeditionsMacro default-targeting, default-Auto-Upgrade, unit-check interval, placement-attempt, and impossibility-threshold controls. A single Advanced Settings dropdown reveals the Step Mode timing, proof checks, and upgrade-readiness check. Recording-mode controls are intentionally absent.
- Test Setup is the compact primary action in the Match Steps header. It assumes Roblox is already on this map's Match Prestart screen, flushes autosave, performs standard camera alignment, and executes the active route through Start Game and its after-start actions. It becomes Stop Test while running; the configured Macro start/stop key also cancels it, and map, route, and step editing remain locked until completion or cancellation.
- In `Place` mode, a left-click on the map creates a Place action at the original-image pixel coordinate using the selected Unit Slot and saved route defaults. Every marker inside the cursor's fixed viewport-space proximity radius hides its label and dims its pin together so crowded markers do not hide the intended point; marker tooltips are not shown, and markers cannot be dragged or deleted. In `Select` mode, empty-map and marker-body clicks do nothing; hovering a marker raises it above neighboring markers and turns its complete label into a red delete button, while dragging only the exact placement dot moves the stable placement without selecting or scrolling the timeline. Nearby non-dragged markers dim during that drag. There is no separate delete icon or label drag affordance. Markers retain one constant screen-space size while the map zoom changes, so zoom reveals spacing instead of enlarging labels. Markers use one compact pin with an upper-left unit label and no routed leader lines. A slot used once displays its number; repeated placements display `1a`, `1b`, and so on. Add Step does not contain Place; it opens a centered owner-modal editor for Delay, Reconfigure, Upgrade, and Sell. Edit uses the same styled field surface for an existing row.
- The timeline always contains one movable Start Game boundary. Steps above it run before start and steps below it run after start.
- Reorder from anywhere on a row except its action buttons. The source leaves a stable gap, a translucent row preview follows the pointer, and one pink insertion line marks every boundary before the first row through after the last row; edge dwell scrolls the owning list or page. Do not show redundant Before/After chips: row position around Start Game is the phase indicator. Color-code each action with a narrow rail and title treatment; light and dark palettes supply separate action colors. Selection uses the themed surface and ink outline; native system-blue ListBox selection chrome is not allowed.
- The authoring action set is Place, Delay, Reconfigure, Upgrade, and Sell. Unit actions reference an earlier slot-labeled placement.
- Deleting a Place row also removes every Reconfigure, Upgrade, and Sell row that references it so the timeline remains valid.
- Route defaults remain in the saved contract. Team and Unit Slot are live authoring controls; Add Step uses the saved defaults and per-step settings appear in its centered dialog.
- Committed edits save atomically under `%LOCALAPPDATA%\LilacMacro\placements`; personal map images remain in the dataset directory.
- Returning to the gallery must not discard committed placement edits.

The canonical route, timeline, coordinate, autosave, and playback-boundary contract is [docs/PLACEMENT-AUTHORING.md](docs/PLACEMENT-AUTHORING.md).

### Settings

Purpose: keybinds, webhooks, Roblox private server links, and other application settings.

- Keep the theme controls as the first fields inside the General / Theme card. Mode is a direct Light/Dark toggle followed by one compact two-row palette grid: ten solid families on the first row and ten gradient families on the second. Each tactile swatch uses the same ink outline and ink-colored hard offset shadow as other controls, has a distinct Light and Dark definition, updates its preview with the active mode, and shows a contained check badge when selected; theme names remain available as tooltips rather than consuming row space.
- Use one Minimize behavior dropdown for Keep visible, Minimize while running, and Minimize on start. Compact layout forces Minimize while running and makes that constraint visible instead of presenting an ineffective choice.
- Treat the standard Roblox 100% UI scale as a startup invariant. Do not expose a UI-scale selector or an option to skip scale preparation.
- Treat the documented Roblox UI/input settings allowlist as a plan-start and private-server-reset invariant. Do not expose per-field overrides; normalization occurs only after the owning Windows session closes Roblox.
- Use one persistent internal category bar: General, Roblox, Discord, Keybinds, and Diagnostics.
- General owns appearance, updates, and local-data controls. Game-setting normalization and fresh Lobby verification are mandatory runtime invariants, not settings.
- General shows the running Macro version as a terse read-only value at the top of Updates and data so installed and local artifacts can be identified without opening build metadata.
- Roblox owns the private-server link and optional local-session target. Do not expose retry counts, rejoin switches, or a way to disable fresh Lobby evidence.
- The private-server link remains visible while editing because it is account-routing configuration, but it stays DPAPI-protected at rest and redacted from diagnostics. Webhooks remain masked. Private-server Test Link and runtime resets convert validated Roblox web/share links to the registered `roblox://` protocol so the browser is never the launch intermediary.
- Local instances presents This desktop plus compact Runner rows with session state, shared/separate configuration, endpoint, `OPEN`, and `REMOVE`. Machine actions remain one terse row: setup, repair, add shared, add separate, and remove all.
- Discord owns webhook and failure-notification fields.
- Keybinds uses a compact name, scope, and binding list.
- Diagnostics owns failure evidence, Deep Debug, local archive cleanup, and default-off automatic error reports. It has no recording or manual diagnostic-upload surface.
- Controls are session-only prototypes unless their current implementation explicitly says otherwise. Theme mode and color-family selection update live and persist with display/update preferences, press-then-key bindings, private-server/webhook values, Discord failure options, Plan state, and local-instance profiles across versioned artifacts. Every macro UI runs on its own desktop; there is no run-target selector. Current Story/Raid Play and Unit inventory bindings may be unset to use verified OCR button navigation. Areas exposes the same optional binding contract for its future runner. Webhook delivery, private-server protocol launch, coordinated updates, and automatic deep-debug diagnostics are connected.

## Visual language

- Continue the existing SquareClaim-inspired Lilac theme rather than introducing a second visual system.
- Use the paper background as the main canvas and the card surface only where a bounded interactive region is necessary.
- Render a gradient theme once across the complete window canvas rather than repeating it per pattern tile. Draw the decorative layer independently: a visible 16-pixel dot field beneath continuous, gently slanted diagonal rules spaced about 80 pixels apart.
- Use ink outlines and hard offset shadows consistently. Do not add soft shadows, glass effects, gradient text, or neon styling. The selected gradient theme may tint the application canvas and primary accent fills; bounded card surfaces, text, destructive actions, status colors, and game-state evidence remain solid and semantic.
- Use pink for the primary action and active working selection, yellow for secondary semantic emphasis, green for confirmed success, and red only for destructive or failed states.
- Plan state chips use dedicated theme tokens: mint Ready, lavender Waiting, amber Cooldown, and dusty-pink Complete. Each theme supplies separate restrained backgrounds and readable text colors; global success and warning fills are not reused.
- Use the existing application typography and icon vocabulary.
- Source every interface icon from Lucide and render it from centralized vector geometry. Do not use font characters as substitute icons.
- Use one implicit thin scrollbar style everywhere: 10-pixel rails, 6-pixel rounded thumbs, no arrow buttons, and pink hover or drag feedback.
- Light and dark modes share the same semantic brush keys and SquareClaim structure. Ten solid families and ten gradient families each provide distinct Light and Dark counterparts. Theme brushes are supplied by the active palette and consumed through dynamic resources so switching mode or palette updates every open surface without rebuilding its controls. Repository policy rejects missing palette counterparts and static theme-brush references.
- Combo boxes use explicit Lilac templates for the closed field and popup items. Native gray fields and system-blue selection are not part of the control vocabulary.
- Recoverable macro-shell errors use one dismissible bottom-right toast with a Lucide alert icon. Do not add inline corner text or native message boxes for placement validation.
- Avoid large descriptive headings. Page names, compact section labels, and control labels are sufficient.

## Navigation and persistence

- **Prototype:** changing tabs changes only the visible page and preserves each page instance.
- **Prototype:** Setup commits autosave, and application shutdown flushes queued Setup writes or surfaces a clear failure.
- **Prototype:** committed Plan changes autosave atomically, preserve the selected plan across launches, and flush before shutdown or Macro start.
- **Planned:** an active macro continues when the user opens Plan, Setup, or Settings.
- **Prototype:** leaving Setup is blocked while Test Setup owns Roblox input; application shutdown cancels it and waits for input cleanup.

## Deliberately unresolved

The following areas are not designed by this document:

- Scheduler/runtime integration
- Complete unattended placement playback and detector integration
- Webhook delivery and protected-secret recovery UX
- Diagnostics capture ownership and recording semantics

Resolve each area in its own design pass without changing the four-tab application structure unless the product requirements change.

The planned runtime module graph is intentionally documented separately in [docs/MACRO-ARCHITECTURE.md](docs/MACRO-ARCHITECTURE.md).
