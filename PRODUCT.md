# Product

**Status: Current product intent.** Capability status is tracked in [docs/PROJECT-STATUS.md](docs/PROJECT-STATUS.md); planned runtime design lives in [docs/MACRO-ARCHITECTURE.md](docs/MACRO-ARCHITECTURE.md).

## Register

product

## Users

LilacMacro serves Windows users testing repeatable Anime Expeditions automation and contributors maintaining its visual evidence. Users build Plans and placements, supervise beta runs, and can keep separate local or managed-runner configurations while the game and its UI continue to change.

## Product Purpose

LilacMacro creates inspectable placement Plans and explicit visual state evidence for bounded automation. Dataset Builder owns capture and dataset authoring. Runtime Lab owns developer-triggered Debug rules, OCR/image comparison, and flow tests that expose matched text and coordinates before sending input. The Macro owns a Lobby-rooted priority scheduler and modular mode runners without weakening those evidence boundaries.

## Brand Personality

Tactile, focused, candid. The interface should carry SquareClaim's bright paper-and-ink energy while remaining a practical desktop instrument that always exposes what it found, changed, captured, and saved.

## Anti-references

Do not resemble a generic dark neon gamer dashboard, translucent glass UI, bland default WPF settings window, or a decorative SaaS control panel. Visual personality must never hide capture state or make destructive and non-destructive actions look alike.

## Design Principles

- Open on Macro and use the fixed top tabs Macro, Plan, Setup, and Settings. Dataset Builder and Runtime Lab remain separate owner tools rather than primary macro-shell navigation.
- Use terse tool labels and live values. Do not add onboarding prose, marketing headlines, safety blurbs, or instructional guide panels.
- Verify every external state before presenting it as complete.
- Public unattended runs recover from runtime anomalies instead of ending: each input episode remains bounded and evidence-gated while the scheduler escalates through reacquisition, Roblox restart, task quarantine, and delayed retry until the user stops it.
- Keep evidence inspectable through exact dimensions, timestamps, hashes, and explicit frame verdicts.
- Make annotation fast: image navigation, box creation, labeling, and notes stay in one workspace.
- Compare OCR models on the same saved crop; confidence is evidence, never authorization by itself.
- Keep image navigation direct: wheel to zoom around the pointer and middle-drag to pan in annotation and OCR-map views.
- Let the OCR map expand to the full canvas when source imagery is unnecessary.
- Let each Story act inherit a shared placement timeline until the owner creates an explicit override.
- Keep F5 dedicated to an active manual session. F6 starts independent timed drafts unless an explicitly armed Debug key chain owns F6 for start/cancel.
- Store personal captures outside the repository and save review changes atomically.

Detailed field rules belong in [docs/GAME-BEHAVIOR.md](docs/GAME-BEHAVIOR.md), placement behavior in [docs/PLACEMENT-AUTHORING.md](docs/PLACEMENT-AUTHORING.md), and local-data handling in [PRIVACY.md](PRIVACY.md).

## Accessibility & Inclusion

The public beta does not claim formal accessibility certification. Preserve native keyboard focus, readable contrast, 100% display-scale guidance for capture accuracy, and text-backed status as required desktop behavior.
