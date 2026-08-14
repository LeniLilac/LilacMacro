# App agent instructions

This file applies to `src/LilacMacro.App`.

- App owns WPF composition, view lifecycle, UI state, and coordination of Core and Windows services. Do not move Win32 or domain policy into code-behind.
- Keep the UI responsive: await I/O, capture, OCR, and persistence; marshal UI mutations through the Dispatcher; never block the UI thread with `.Wait()` or `.Result`.
- Preserve one owner per long-running operation and keep cancellation/disposal explicit.
- Flush queued placement and review writes before changing ownership or closing. Surface save failures instead of silently discarding edits.
- Use semantic brushes from both `ThemeColors` dictionaries through `DynamicResource`; never capture a theme brush with `StaticResource`. Light and dark mode must update the live visual tree without reconstructing pages. Repository policy enforces matching palette keys and dynamic consumers.
- Source every UI icon from centralized Lucide vector geometry. Do not use Unicode characters, fonts, emoji, or ad hoc SVG paths as icon substitutes.
- Apply the shared thin scrollbar style to every scrollable surface, including nested lists and combo-box popups.
- Preserve the terse owner-tool language in [DESIGN.md](../../DESIGN.md): no onboarding copy, marketing headings, guide cards, or accessibility-project expansion.
- Keep Macro, Plan, and Settings prototype controls visibly separate from implemented runtime behavior. In-memory preview interactions must never imply that scheduler, Roblox input, persistence, webhooks, or updates are connected.
- `MainWindow` serves two focused launch profiles: Dataset Builder owns Capture/Review/Datasets, and Runtime Lab owns Debug/Wire Test. Do not leak pages across those navigation boundaries or duplicate their shared services.
- Add pure policies to Core and test them there instead of burying deterministic behavior in WPF event handlers.
- Give every transition source and modal/destination a distinct dataset-owned state and ROI. Check the destination first, inspect the source only after a destination miss, and authorize clicks only from that state's fresh bounds. Never merge overlapping layers into a broad OCR crop or choose duplicate labels by topmost/bottommost screen order.
- Route every semantic search area through an explicit bundled annotation and the runtime evidence catalog described in [`../../docs/RUNTIME-EVIDENCE.md`](../../docs/RUNTIME-EVIDENCE.md). If annotation intent is unclear, ask the owner before coding.
- Do not use computer-control tooling to exercise the app or Roblox. Build and test automatically, then give the owner a focused manual visual/behavior checklist.
