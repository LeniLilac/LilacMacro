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
| Play UI | `play-ui-20260802-191143`, frame 1 | Any 2 distinct mode groups: Story, Raid, Challenge, Expedition |
| Event select | `event-select-20260802-224426`, frame 1 | Exact normalized Events, Back, and Calendar boxes |
| Areas UI | `areas-ui-20260802-231943`, frame 1 | Areas plus any 2 distinct: Upgrade, Gamemode, Lobby, Shop, Expedition |
| Unit inventory | `unit-inventory-detect-to-teams-swap-ui-20260802-222311`, frame 1 | Teams plus one: Unequip, Unequip All, Quick, Quick Sell |
| Team swap | `team-swap-20260802-222627`, frame 1 | Unit Teams, Save Team/Save, and Load Team/Load |
| Team load confirm | `team-swap-confirm-flow-20260802-223223`, frame 2 | Confirm and Cancel |
| Include equipment | `team-swap-confirm-flow-20260802-223223`, frame 3 | Include, Exclude, and Cancel |
| Challenge type | `challenge-type-picker-20260802-215826`, frame 1 | Challenge/Challenges, Daily Challenge, and Weekly Challenge |
| Challenge selected, available | `challenge-set-1-20260807-002022`, global ROI | Topmost Challenges, one supported map, Back, Select Stage; purple Enter Matchmaking is visual evidence in hybrid mode |
| Challenge selected, cooldown | `challenge-set-3-20260807-003809`, global ROI | Topmost Challenges, one supported map, Back, fuzzy-prefix Available in, and Enter Matchmaking; Back, Available in, and Enter Matchmaking must occupy the same live bottom action row. Gray Enter Matchmaking is visual evidence in hybrid mode |
| Story map | `story-map-picker-20260802-192129`, frame 2 | Any 2 distinct configured Story map groups |
| Raid map | `raid-map-picker-20260802-215104`, frame 1 | Spirit City, Spirit, or City |
| Expedition map | `expedition-map-picker-20260802-220435`, frame 1 | School Grounds, Flower Forest, and Rose Kingdom |
| Story act | `story-map-act-picker-play-ui-20260802-193045`, frame 1 | Story, Select Stage, and Enter/Matchmaking/Enter Matchmaking |
| Raid act | `raid-map-act-picker-20260802-215448`, frame 1 | Raid, Select Stage, and Enter/Matchmaking/Enter Matchmaking |
| Match preview | `match-preview-general-20260802-211007`, frame 1 | Start plus one group: Change Map, Disband, Invite Players, Leave Party |
| Match prestart | `match-prestart-20260802-212342`, frame 1 | Two separate Start Game boxes |
| Defeat | `defeat-screen-general-20260802-213156`, frame 1 | Defeat plus any 2 result/action groups |
| Victory | `victory-screen-general-20260802-214302`, frame 1 | Victory plus any 2 result/action groups |

Result/action groups are Repeat Stage/Repeat, View Party/Party, Game Stats, Gained Rewards, Clear Time, Total Yen, Total Kills, and Total Damage.

## Root and category actions

- In the Story/Raid runtime, Open Units and Open Play use their session keybind when set. If the keybind is unset, they click the live target box's top-center. Both paths start from verified Lobby, wait 5 seconds, and verify the destination state. Debug Open Events and Open Areas currently use the live target box; the optional Areas key is reserved for the future Areas runner.
- From verified Play UI, Story, Raid, Challenge, or Expedition clicks the matched live mode box center. Mode aliases include their configured `gamemode` descriptors.
- Event select clicks the center of a live Villain Invasion/Invasion box.
- Areas UI clicks the center of the leftmost live matching category box so a similarly named content heading farther right cannot own input.
- Unit inventory clicks the live Teams center only after the supporting inventory action is present.
- Closing Unit inventory after a team change reuses its configured key. If unset, the fallback first verifies Team Swap, reacquires the live Units text from the menu ROI, clicks its top-center, and verifies Lobby.

## Team swap

- Custom team names do not own selection. Rows are formed from the scale-relative Unit Teams title and repeated Save/Load OCR geometry; split `Save` + `Team` and `Load` + `Team` results are merged before pairing.
- A slow session setup clamps the list to each endpoint twice, captures only the narrow scrollbar strip, and accepts the gray thumb only when its X/shape is stable, its endpoint Y positions repeat, and it moves between endpoints. The resulting client-relative title, row pitch, top/bottom thumb bounds, endpoint Load positions, and middle drag are retained for the current macro session.
- Fast repeats translate cached points from the current live Unit Teams title and reject a materially different title scale. Teams 1-2 first use one 10000-unit upward clamp and click their top-relative Load positions. Teams 3-5 use the same upward clamp, then drag the cached top thumb to its calibrated midpoint in 180 ms and click a row-pitch-derived middle position. Teams 6-8 use one 10000-unit downward clamp burst. No scrollbar image detection runs on this fast path.
- The top and bottom endpoint layouts each require two complete Save/Load rows. Team 3's clipped top Load is never extrapolated into the non-interactive area; its target is derived at the calibrated middle viewport. Bottom teams remain derived from the lower endpoint row pitch. Arbitrary team-name text is ignored.
- Confirm may click only while Cancel is visible; Include may click only while both Exclude and Cancel are visible. A missing confirmation invalidates the session calibration, and ambiguous geometry fails closed.

## Observed transitions

- An action that owns `source -> destination` verifies the destination first. If destination evidence is absent, it immediately checks the source: destination present means success, source retained means the action was not applied, and neither state means an indeterminate transition that fails closed. Destination evidence wins when an overlay leaves both states visible.
- Lobby menu transitions, closing Units, and the Load Team -> Confirm -> Include -> Team Swap flow use this shared policy. The source fallback runs only after destination failure so successful fast paths do not pay for redundant OCR.

## Map and act selection

- Story map actions choose the leftmost live matching map label and click its top-center, preventing a larger detail heading farther right from owning input. Fairy King Forest and King's Tomb first scroll down 2000 wheel units over 2 seconds at client center.
- Raid map selection accepts Spirit City aliases and clicks the matched box top-center.
- Expedition requires all three map groups. It clicks the requested map's leftmost text center, waits 250 ms, and captures fresh evidence. The selected-map heading must appear above and right of the list label. The topmost Difficulty and lowest Select Stage boxes form the scale anchors. Minus clicks 3 times; Plus clicks 0, 1, or 2 times for difficulty 1, 2, or 3; then the live Select Stage center clicks.
- Challenge selection derives Trait, Stat, and Sprite row chevrons from the exact Challenges, Daily Challenge, and Weekly Challenge layout anchors. Reward text does not own input.
- Challenge selected-state map aliases and coarse spatial rules are shared with Story map recognition. Available clicks the fresh Select Stage center. Cooldown clicks the fresh Back center and verifies the type picker before trying the next enabled type.
- Challenge attempts are scoped to the UTC half-hour reset epoch. A type seen cooling down in a later epoch than its previous cooldown observation is treated as 10/10 daily-limited until UTC midnight. This evidence is stored per type; an available observation clears it, and one exhausted type does not block other enabled types.
- Story act derives 7 uniformly spaced act points from exact topmost Story and Select Stage anchors. It clicks Act 1-5, Infinite, or Mastery, waits 250 ms, and requires fresh matching confirmation. Acts 1-5 derive Normal/Hard points from the confirmed layout without OCR of those labels. Infinite and Mastery skip difficulty. The final click uses the fresh Select Stage center.
- Raid act uses the equivalent Raid and Select Stage anchors for 3 act rows, requires fresh Act confirmation, has no difficulty click, and finishes at the fresh Select Stage center.

## Match actions

- Match preview clicks the live Start center after Start plus one supporting group owns the state.
- Match prestart selects the lowest of the two live Start Game rectangles and clicks its center.
- Defeat and Victory click the live Repeat or Repeat Stage center only after their result heading and two supporting groups own the state.

Direct Repeat Stage remains Debug evidence. The planned priority scheduler instead returns through a private-server Lobby reset; see [Macro architecture](MACRO-ARCHITECTURE.md).

## Story, Raid, and Challenge Wire Test

- Wire Test is an owner-triggered developer diagnostic for Story, Raid, or Challenge navigation through verified Match Prestart.
- It reuses the state ownership and clicks above, polls destinations only within a 20-second bound, and blocks at the first state that cannot be freshly verified.
- After Include Equipment, it requires Team Swap again, taps the configured Units key, and requires Lobby before opening Play.
- Capture hotkeys are suppressed while the wire owns the workflow. Stop or application shutdown cancels the run; input cleanup remains owned by the shared Windows input service.
- By default, a successful wire ends at verified Match Prestart. `Run placements + match` explicitly continues through the effective authored route, Start Game boundary, after-start actions, and verified Victory or Defeat. `Repeat Stage` remains explicit Debug evidence and is not the canonical priority loop.

## Selected-unit and Upgrade evidence

- The first physical placement uses bounded Priority, Sell, and DPS OCR observations to calibrate the selected-unit panel at the current UI scale. Three consistent layouts are required.
- `DPS ???` rejects a phantom placement. Numeric DPS ending in `/s` is physical selection evidence. Later rows capture only the compact derived panel regions and use tiny DPS OCR only for physical-versus-phantom proof.
- Upgrade readiness is classified from two compact RGB regions derived from the live Priority/Sell geometry: green main control means affordable, gray main plus the normal-width gray extension means unaffordable, and gray main plus an expanded gray extension means maxed.
- Unaffordable waits are bounded. Unknown color evidence fails closed. Maxed stops remaining Upgrade presses for that action.
- Runtime Lab Debug and Wire Test expose `OCR` and `IMAGE + OCR FALLBACK`. `OCR` verifies every check through OCR and does not build or run image profiles.
- `IMAGE + OCR FALLBACK` first evaluates the state's currently selected `IMAGE` elements from saved per-user profiles and their last OCR-verified bounds. Only reliable, unambiguous matches count, and their labels must satisfy the same required or N-of state rule; missing optional elements do not block an otherwise complete state. Incomplete image evidence immediately falls back to OCR.
- A successful OCR fallback captures five compact GPU-region samples over a short burst, saves immutable refreshed profiles plus atomic last-verified locators, captures one fresh bounded search region, and displays OCR/image bounds, timing, match score, strategy, and agreement.
- Image-first success may satisfy a diagnostic state check, but it never supplies click coordinates. Every Debug or Wire action still performs its own fresh OCR verification and uses live OCR-owned bounds or freshly verified relational anchors before input.

## Camera and key-chain evidence

- Debug Camera Align verifies Roblox, scrolls down 5000 wheel units over 1 second, temporarily toggles standard Left Shift lock, then performs a straight-down 5000-relative-unit right-button camera drag over 1 second. Story/Raid match setup uses the session Shift Lock binding for the same balanced toggle. Cleanup releases input, restores the cursor, and balances temporary Shift Lock.
- The Debug key chain accepts at most 32 physical-key holds, each 1-120000 ms, with at most 600000 ms total hold time. Arm + Focus gives it temporary ownership of F6; F6 starts and cancels it. F6 cannot be a chain key, and cancellation releases the current key.

Any new field observation must update this ledger and its deterministic Core tests in the same change.
