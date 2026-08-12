# Game behavior ledger

**Status: Prototype field evidence.** This document describes current, owner-triggered OCR Debug behavior. It does not describe an unattended macro loop.

## Global Debug contract

- Roblox must be a freshly verified `1366 x 700` client area.
- Every state loads the first annotation from the configured finalized dataset frame as its OCR region of interest (ROI).
- Runtime Lab Debug defaults to OCR. Its `IMAGE + OCR FALLBACK` option first evaluates the state's selected `IMAGE` elements from saved per-user profiles; incomplete image evidence runs `PP-OCRv6_small_rec` with its paired detector inside the dataset ROI.
- Normal matching keeps only ASCII `A-Z`, `a-z`, and `0-9`, lowercases letters, removes spacing and symbols, and accepts the normalized alias as a substring.
- Exact states require normalized equality. Repeated states require separate OCR rectangles.
- A state must pass before its action. Actions still require fresh OCR-owned target bounds or freshly verified relational anchors even when a preceding check passed through image evidence.
- Roblox is revalidated immediately before input. Missing or ambiguous evidence blocks input; there are no static-coordinate fallbacks, automatic retries, or unattended loops.

Dataset paths below are relative to `Documents\LilacMacro Datasets`.

## State ownership

| State | Dataset and ROI | Required evidence |
|---|---|---|
| Lobby | `lobby-20260802-185951`, frame 1 | Any 2 distinct: Store, Units, Items, Quests, Summon, Areas, Play, Events |
| Play UI | `play-ui-20260802-191143`, frame 1; `play-ui-mode-picker-update-1-0-20260810-151955` is update evidence | Any 2 distinct mode groups: Story, Raid, Challenge, Expedition, Tower |
| Event select | `event-select-20260802-224426`, frame 1 ROI; `update-1-0-event-ui-{small,standard,large}-20260810-*` cross-scale evidence | Exact normalized Events, Back, and Calendar boxes |
| Areas UI | `areas-ui-20260802-231943`, frame 1 | Areas plus any 2 distinct: Upgrade, Gamemode, Lobby, Shop, Expedition |
| Unit inventory | `unit-inventory-detect-to-teams-swap-ui-20260802-222311`, frame 1 | Teams plus one: Unequip, Unequip All, Quick, Quick Sell |
| Team swap | `team-swap-20260802-222627`, frame 1 | Unit Teams, Save Team/Save, and Load Team/Load |
| Team load confirm | `team-swap-flow-revised-20260808-054531`, frame 2 | Load Team, Confirm, and Cancel |
| Team save confirm guard | `team-swap-flow-revised-20260808-054531`, frame 1 | Save Team, Confirm, and Cancel |
| Include equipment | `team-swap-flow-revised-20260808-054531`, frame 3 | Include, Exclude, and Cancel |
| Challenge type | `challenge-type-picker-20260802-215826`, frame 1 | Challenge/Challenges, Daily Challenge, and Weekly Challenge |
| Challenge selected, available | `challenge-set-1-20260807-002022`, global ROI | Topmost Challenges, one supported map, Back, Select Stage; purple Enter Matchmaking is visual evidence in hybrid mode |
| Challenge selected, cooldown | `challenge-set-3-20260807-003809`, global ROI | Topmost Challenges, one supported map, Back, fuzzy-prefix Available in, and Enter Matchmaking; Back, Available in, and Enter Matchmaking must occupy the same live bottom action row. Gray Enter Matchmaking is visual evidence in hybrid mode |
| Story map | `story-map-picker-20260802-192129`, frame 2; `story-map-picker-update-1-0-20260810-151845` is update evidence | Any 2 distinct configured Story map groups, including East Town |
| Raid map | `raid-map-picker-20260802-215104`, frame 1 | Spirit City, Spirit, or City |
| Expedition map | `expedition-map-picker-20260802-220435`, frame 1; `expedition-map-picker-update-1-0-20260810-151505` is update evidence | School Grounds, Flower Forest, Rose Kingdom, and East Town |
| Story act | `story-map-act-picker-play-ui-20260802-193045`, frame 1 | Story, Select Stage, and Enter/Matchmaking/Enter Matchmaking |
| Raid act | `raid-map-act-picker-20260802-215448`, frame 1 | Raid, Select Stage, and Enter/Matchmaking/Enter Matchmaking |
| Match preview | `match-preview-general-20260802-211007`, frame 1 | Start plus one group: Change Map, Disband, Invite Players, Leave Party |
| Match prestart | `match-prestart-20260802-212342`, frame 1 | Two separate Start Game boxes |
| Defeat | `defeat-screen-general-20260802-213156`, frame 1 | Defeat plus any 2 result/action groups |
| Victory | `victory-screen-general-20260802-214302`, frame 1 | Victory plus any 2 result/action groups |

Result/action groups are Repeat Stage/Repeat, View Party/Party, Game Stats, Gained Rewards, Clear Time, Total Yen, Total Kills, and Total Damage.

## Root and category actions

- Runtime Lab Debug exposes Open Play, Open Units, Open Events, and Open Areas from Lobby. Open Units clicks the live Units box top-center, waits 5 seconds, and verifies Unit inventory while also checking whether Lobby was retained. In the Story/Raid runtime, Open Units and Open Play use their configured keybind when set; when unset, they use the same live top-center target path. Debug Open Events and Open Areas use live target boxes; the optional Areas key is reserved for the future Areas runner.
- From verified Play UI, Story, Raid, Challenge, Expedition, or Tower clicks the matched live mode box center. Mode aliases include their configured `gamemode` descriptors. Tower currently has only this owner-triggered Debug selection action; no Tower task runner is defined.
- Event select supports Villain Invasion, Boss Bounty, and Guess That Unit. It clicks the center of the selected destination's live OCR box in the sidebar.
- The selected event heading at the upper left changes color with the active event. It is OCR-only evidence and must not own or refresh an image profile. Stable Back and Calendar controls may provide image corroboration for Event Select, while all destination click coordinates remain live OCR-owned.
- Areas UI clicks the center of the leftmost live matching category box so a similarly named content heading farther right cannot own input.
- Unit inventory clicks the live Teams center only after the supporting inventory action is present.
- Closing Unit inventory after a team change reuses its configured key. If unset, the fallback first verifies Team Swap, reacquires the live Units text from the menu ROI, clicks its top-center, and verifies Lobby.

## Startup UI-scale normalization

- At Main Macro plan start and every private-server Lobby reset, the owning Windows session terminates its Roblox client process tree, waits for process exit, normalizes the current profile's `GlobalBasicSettings_13.xml`, converts the validated private-server share/link code to a registered `roblox://` target, and launches Roblox directly without opening a browser. The lifecycle uses at most two process-tree termination passes and rediscovers current-session clients before each pass, covering Roblox's observed PID handoff without waiting on its close-confirmation surface. Clients in other Windows sessions are not targeted.
- The XML edit requires exactly one structurally valid `UserGameSettings` item. It atomically normalizes only text size, chat/stats/player-list visibility, fullscreen, classic camera, keyboard/mouse movement, Shift Lock control mode, inverted camera, scalar plus first/third-person mouse sensitivity, UI-navigation key capture, VR, profiler overlays, preferred transparency, and reduced motion. Unknown fields, graphics quality, FPS cap, volume, window placement, identifiers, and unrelated settings are preserved. A transient sibling backup supports interrupted replacement recovery and is removed after a successful reread.
- Every full Macro UI and Runtime Lab Wire Test always normalize rendered UI scale before task navigation. Each Macro instance repeats it after every restarted private-server join. Runtime Lab Debug exposes the identical rendered-scale operation for owner testing. There is no setting that disables normalization or fresh Lobby verification.
- Normalization first requires two consecutive fresh Lobby observations. Two stable panel observations retain an already-open Settings surface; otherwise the fixed Settings button is verified through its dark control and centered multi-quadrant gear structure before it is clicked. Both the filled hover glyph and outlined closed glyph are accepted; isolated bright marks and incomplete glyphs fail closed. Settings then requires the Settings heading, search control, and navigation rail. It searches for `UI Scale`, then requires the `Miscellaneous` heading, `UI Scale` label, and its descriptive row text before any value-field click.
- The displayed numeric value is not OCR evidence and is never treated as the target. A numeric input of `1.00` can render at different physical UI sizes on different display/session configurations.
- The red close control plus independent cyan left, right, and bottom panel borders own the rendered panel geometry. The normalizer enters a known candidate, waits for two stable observations, and measures the resulting rendered scale. If it is outside `0.98` through `1.02`, the next candidate is `candidate / observed rendered scale`, rounded to two decimals and clamped to the supported `0.80` through `1.20` input range. At most five candidates are entered; non-convergence fails the startup operation without blind input.
- A successful candidate is cached under the current Windows profile and Windows session. The console owner and local-runner account therefore use separate files, and separate RDP sessions use separate entries. The cache is only a hot-path hint: every run measures the resulting panel geometry, stale hints continue through full feedback calibration, and only a freshly verified canonical result updates the cache.
- Before closing, the semantic UI Scale row and canonical panel geometry are reacquired. The close click requires fresh structural evidence, and two consecutive Lobby observations complete the operation.
- `settings-ui-scale-40-scale-datapoints-20260810-182820` supplies 40 chronological panel samples spanning rendered scales `0.80` through `1.20`. `settings-ui-scale-p2-20260810-182357` and `settings-ui-setup-config-p1-20260810-182027` provide Settings search/row and surrounding configuration evidence. Owner live acceptance across desktop and local-runner sessions remains required.

## Team swap

- Custom team names do not own selection. Rows are formed from the scale-relative Unit Teams title and repeated Save/Load OCR geometry. Horizontally adjacent same-line OCR fragments are composed, including the small box overlap produced by outlined fonts, so split `Unit` + `Teams`, `Save` + `Team`, and `Load` + `Team` results retain the same semantic ownership; separated rows, excessive overlap, or distant fragments do not compose.
- A slow session setup clamps the list to each endpoint, captures only the narrow scrollbar strip, and accepts the gray thumb only when its X/shape is stable, its endpoint Y positions repeat, and it moves between endpoints. From the verified top endpoint it applies one 600-unit wheel probe, measures the resulting normalized thumb position, and derives the scale-specific wheel amount for 50% travel. The client-relative title, row pitch, top/bottom thumb bounds, endpoint Load positions, and derived middle wheel amount are retained for the current macro session.
- Fast repeats translate cached points from the current live Unit Teams title and reject a materially shifted title while tolerating OCR box-size variation at small UI scales. Teams 1-3 first use one 10000-unit upward clamp and click top-relative Load positions; Team 3 is lifted from its hidden text center into the visible green Load sliver. Teams 4-5 reset to top, issue the cached wheel burst, and sample the narrow scrollbar strip for a bounded period. The newest consecutive stable pair must land within 40%-60%; captures taken while Roblox is still easing the list are ignored. Their Load position is recalculated from the observed normalized position rather than assuming an exact midpoint. Teams 6-8 use one 10000-unit downward clamp burst. No repeat OCR is added to the middle fast path.
- The top and bottom endpoint layouts each require two complete Save/Load rows. Team 3 never uses its hidden OCR text center. Bottom teams remain derived from the lower endpoint row pitch. Arbitrary team-name text is ignored.
- A Load confirmation requires the Load Team title plus Confirm and Cancel. Save Team is explicit negative evidence: the runner clicks Cancel, verifies the Save dialog closed, clamps back to the top, and permits one bounded retry. Include may click only while both Exclude and Cancel are visible. Source and destination are both checked after each action; transient OCR misses receive at most three fresh observations. Indeterminate dialog evidence fails closed without discarding otherwise valid scrollbar geometry. When the Team Swap source remains after a Load click, the runner clamps to the top, obtains fresh state, layout, scrollbar, and target evidence, then retries once with the retained calibration. It recalibrates only when that fresh scrollbar or target geometry fails validation.
- Runtime Lab `Scroll Test` is temporary owner evidence for choosing the Teams 4-5 fast path. It calibrates top/bottom thumb bounds once, then independently repeats either the current midpoint drag or a downward wheel schedule. A Scroll schedule starts at the configured units and adds the configured increment for every following trial, so `600`, `10`, and `10` trials execute `600` through `690`. Each trial begins with the accepted 10000-unit upward clamp, saves full before/after client frames, measures the stable thumb's normalized endpoint position from two compact ROI captures, and records requested units beside the observation in the UI and `results.json`. A final-reset frame remains under local diagnostics.
- Runtime Lab `Team Swap Test` assumes Unit Teams is already open. It generates balanced randomized blocks of Teams 1-8, runs the same production Load/Confirm/Include path for each requested trial, preserves session calibration between trials, and shows target team, pass/fail, elapsed time, and terminal status. Cached scrollbar capture regions are translated from the current Unit Teams title and clipped to the verified client before capture; a stale cached target triggers one same-trial recalibration. Ordinary failed trials are recorded and the batch continues. Stop and F6 cancel the active batch without recording its interrupted trial as a swap failure. Deep debug records the random seed and complete per-trial transition evidence so a run can be reproduced and classified.
- The 2026-08-08 owner A/B run established that scrollbar dragging is not a reliable fast path. Twenty identical midpoint drags from verified top-bound frames landed from 28.8% to 49.4% with 7.46 percentage-point standard deviation. The endpoints occurred in discrete 10-11-pixel thumb steps, consistent with Roblox consuming a variable number of the synthetic drag updates before button release. Twenty identical 600-unit wheel runs landed at 65.4% with the same `[1049,350,6,123]` thumb bounds every time.
- Three wheel ramp runs at distinct UI geometries completed 590 measured trials with zero reverse steps and linear fits above `R² 0.99997`. Full travel differed by geometry at approximately 736, 916, and 1106 wheel units, so a global fixed amount is invalid even though scrolling is deterministic within one geometry. Production therefore calibrates wheel units from a live probe and verifies the compact thumb ROI before clicking; it does not drag.
- The owner supplied three 500-frame `1366 x 700` scrollbar-travel datasets at small, standard, and large Roblox UI scales. Their chronological frames cover the top, broad middle, and bottom portions of the same Unit Teams control and support the accepted 40%-60% middle landing band.

## Observed transitions

- An action that owns `source -> destination` verifies the destination first. If destination evidence is absent, it immediately checks the source: destination present means success, source retained means the action was not applied, and neither state means an indeterminate transition that fails closed. Destination evidence wins when an overlay leaves both states visible.
- Lobby menu transitions, closing Units, and the Load Team -> Confirm -> Include -> Team Swap flow use this shared policy. The source fallback runs only after destination failure so successful fast paths do not pay for redundant OCR.

## Map and act selection

- Story map actions choose the leftmost live matching map label and click its top-center, preventing a larger detail heading farther right from owning input. Fairy King Forest and King's Tomb first scroll down 2000 wheel units over 2 seconds at client center.
- Raid map selection accepts Spirit City aliases and clicks the matched box top-center.
- Expedition requires all three map groups. It clicks the requested map's leftmost text center, waits 250 ms, and captures fresh evidence. The selected-map heading must appear above and right of the list label. The topmost Difficulty and lowest Select Stage boxes form the scale anchors. Minus clicks 3 times; Plus clicks 0, 1, or 2 times for difficulty 1, 2, or 3; then the live Select Stage center clicks.
- Expedition reward-pool parsing and reroll optimization are Planned and do not currently authorize input. The field sample, provisional thresholds, compact-ROI OCR design, and post-update validation checklist are canonical in [Expedition reward optimization](EXPEDITION-REWARD-OPTIMIZATION.md).
- Challenge selection derives Trait, Stat, and Sprite row chevrons from the exact Challenges, Daily Challenge, and Weekly Challenge layout anchors. Reward text does not own input.
- Challenge selected-state map aliases and coarse spatial rules are shared with Story map recognition. Available clicks the fresh Select Stage center. Cooldown clicks the fresh Back center and verifies the type picker before trying the next enabled type.
- Challenge attempts are scoped to the UTC half-hour reset epoch. A type seen cooling down in a later epoch than its previous cooldown observation is treated as 10/10 daily-limited until UTC midnight. This evidence is stored per type; an available observation clears it, and one exhausted type does not block other enabled types.
- Story act derives 7 uniformly spaced act points from exact topmost Story and Select Stage anchors. It clicks Act 1-5, Infinite, or Mastery, waits 250 ms, and requires fresh matching confirmation. Acts 1-5 derive Normal/Hard points from the confirmed layout without OCR of those labels. Infinite and Mastery skip difficulty. The final click uses the fresh Select Stage center.
- Raid act uses the equivalent Raid and Select Stage anchors for 3 act rows, requires fresh Act confirmation, has no difficulty click, and finishes at the fresh Select Stage center.

## Match actions

- Match preview clicks the live Start center after Start plus one supporting group owns the state.
- Match prestart selects the lowest of the two live Start Game rectangles and clicks its center. Roblox may automatically start after its 60-second prestart timer; placement playback still finishes all authored prestart actions, then treats the Start boundary as already satisfied only after three fresh Start-screen misses plus selected-unit runtime evidence. It then continues into after-start actions. Missing Start evidence without independent runtime evidence fails closed.
- Defeat and Victory click the live Repeat or Repeat Stage center only after their result heading and two supporting groups own the state.

Direct Repeat Stage remains Debug evidence. The planned priority scheduler instead returns through a private-server Lobby reset; see [Macro architecture](MACRO-ARCHITECTURE.md).

## Story, Raid, and Challenge Wire Test

- Wire Test is an owner-triggered developer diagnostic for Story, Raid, or Challenge navigation through verified Match Prestart.
- It reuses the state ownership and clicks above, polls destinations only within a 20-second bound, and blocks at the first state that cannot be freshly verified.
- After Include Equipment, it requires Team Swap again, taps the configured Units key, and requires Lobby before opening Play.
- Capture hotkeys are suppressed while the wire owns the workflow. Stop or application shutdown cancels the run; input cleanup remains owned by the shared Windows input service.
- By default, a successful wire ends at verified Match Prestart. `Run placements + match` explicitly continues through the effective authored route, Start Game boundary, after-start actions, and verified Victory or Defeat. `Repeat Stage` remains explicit Debug evidence and is not the canonical priority loop.

## Selected-unit and Upgrade evidence

- The first selected placement uses bounded Priority, Sell, and DPS OCR observations to calibrate the selected-unit panel at the current UI scale. Three consistent configurable layouts are required.
- `DPS ???` identifies a phantom placement whose Targeting, Auto Upgrade priority, and Sell controls remain actionable. Place configuration, Reconfigure, and Sell may proceed after stable phantom-panel proof. Upgrade remains forbidden until numeric DPS ending in `/s` proves a physical unit. Later rows capture only the compact derived panel regions and use tiny DPS OCR for physical-versus-phantom ownership.
- Upgrade readiness is classified from two compact RGB regions derived from the live Priority/Sell geometry: green main control means affordable, gray main plus the normal-width gray extension means unaffordable, and gray main plus an expanded gray extension means maxed.
- Unaffordable waits are bounded. Unknown color evidence fails closed. Maxed stops remaining Upgrade presses for that action.
- Runtime Lab Debug and Wire Test expose `OCR` and `IMAGE + OCR FALLBACK`. `OCR` verifies every check through OCR and does not build or run image profiles.
- `IMAGE + OCR FALLBACK` first evaluates the state's currently selected `IMAGE` elements from saved per-user profiles and their last OCR-verified bounds. Only reliable, unambiguous matches count, and their labels must satisfy the same required or N-of state rule; missing optional elements do not block an otherwise complete state. Incomplete image evidence immediately falls back to OCR.
- A successful OCR fallback captures five compact GPU-region samples over a short burst, saves immutable refreshed profiles plus atomic last-verified locators, captures one fresh bounded search region, and displays OCR/image bounds, timing, match score, strategy, and agreement.
- Image-first success may satisfy a diagnostic state check, but it never supplies click coordinates. Every Debug or Wire action still performs its own fresh OCR verification and uses live OCR-owned bounds or freshly verified relational anchors before input.

## Camera and key-chain evidence

- Debug Camera Align verifies Roblox, scrolls down 5000 wheel units over 1 second, temporarily toggles standard Left Shift lock, then performs a straight-down 5000-relative-unit right-button camera drag over 1 second. Story/Raid match setup uses the session Shift Lock binding for the same balanced toggle. Cleanup releases input, restores the cursor, and balances temporary Shift Lock.
- Setup `Test Setup` assumes the selected map is already at Match Prestart. It flushes the active placement route, applies the same session-key camera alignment, then runs every authored action around the required Start Game boundary through the shared production placement service. Guaranteed authored delays before that boundary are capped at 30000 ms. An automatic game start does not interrupt the remaining prestart sequence; the shared boundary policy verifies that transition and then continues after-start actions. It does not navigate, load a team, wait for a terminal result, or repeat. Stop and application shutdown cancel it; Setup navigation is blocked until it finishes or stops.
- The Debug key chain accepts at most 32 physical-key holds, each 1-120000 ms, with at most 600000 ms total hold time. Arm + Focus gives it temporary ownership of F6; F6 starts and cancels it. F6 cannot be a chain key, and cancellation releases the current key.

Any new field observation must update this ledger and its deterministic Core tests in the same change.
