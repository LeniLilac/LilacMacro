# OCR and vision

**Status: Current OCR implementation, Implemented adaptive-anchor foundation, and Planned runtime integration.**

## Current OCR pipeline

LilacMacro installs OCR into an isolated Python 3.12 environment at `%LOCALAPPDATA%\LilacMacro\ocr`. The setup script pins PaddlePaddle 3.2.0 and PaddleOCR 3.7.0. It installs one runtime at a time:

```powershell
./scripts/Setup-Ocr.ps1 -Device cpu
./scripts/Setup-Ocr.ps1 -Device gpu
```

GPU setup uses the official CUDA 12.6 package and verifies that Paddle sees a CUDA device. Running setup for one device removes the other Paddle runtime, so the device marker and installed package remain consistent.

The allowlisted model pairs are:

| UI model | Detector | Recognizer |
|---|---|---|
| PP-OCRv6 small | `PP-OCRv6_small_det` | `PP-OCRv6_small_rec` |
| PP-OCRv6 tiny | `PP-OCRv6_tiny_det` | `PP-OCRv6_tiny_rec` |

Review OCR runs detection only inside a manual annotation crop, recognizes each detected line, then shifts every child rectangle back into original-frame coordinates. Runtime Lab Debug uses the small pair inside a dataset-owned ROI on a fresh `1366 x 700` capture when OCR is selected or image evidence falls back.

Review exposes every detected line inside the manually drawn search area as a selectable candidate. Marking `OCR` or `IMAGE` persists those roles directly on the child OCR result without creating another manual annotation or requiring a typed label. A coarse region can be linked globally across frames while its OCR trials stay frame-specific. Agents and future runtime integration do not infer relevance from every detected line merely being present.

Each relevant line can be assigned to `REQUIRED`, `POOL`, or `IGNORE`; the parent stores the minimum number of distinct pool phrases required in addition to every required phrase. Review also infers a spatial selector for duplicate normalized candidates—such as leftmost or topmost—inside the coarse region. The owner can override it with any extreme selector or with same-row/nearest-anchor relations. These policies are element-agnostic and stored in the dataset rather than compiled into a game-state class.

Core can apply the stored selector to fresh OCR candidates. `ANY` succeeds only for one phrase match, extreme selectors require a unique extreme, and relational selectors require their named anchor; ambiguity or a tie returns no target.
The session-only `HIDE UNCHECKED` filter hides unmarked child lines from both the candidate list and OCR visualization; it never hides or changes the manually drawn parent search area.

Each OCR-evidence result defaults to normalized `exact` matching. The owner may toggle `FUZZY` for phrases with at least eight ASCII letters or digits. The shared bounded matcher removes case, spacing, and symbols, then uses normalized Levenshtein similarity with a default `0.78` threshold. Short text remains exact even if a malformed persisted profile requests fuzzy matching; this prevents permissive matches on small labels. Match mode belongs to the result data rather than element-specific code.

## Normalization and evidence

The Debug rule engine:

1. retains only ASCII letters and digits;
2. lowercases letters;
3. thereby removes spaces and symbols;
4. normally accepts an alias when the normalized OCR box contains it.

Some states require exact normalized boxes, a required first anchor, or multiple separate boxes. See [Game behavior](GAME-BEHAVIOR.md). Confidence helps inspect evidence but never substitutes for the required state structure or fresh target coordinates.

## CPU, GPU, and resident workers

- The selected device is stored with each OCR trial as `cpu` or `gpu:0`.
- One-shot mode starts a helper process for a run and exits afterward.
- `KEEP LOADED` starts one persistent helper and caches detector/recognizer pipelines by model and device for the app session.
- Requests and responses use a unique temporary channel with a hard deadline, worker-exit detection, bounded JSON, and cleanup.
- Turning `KEEP LOADED` off or exiting the app terminates the worker and releases its cached pipelines.
- Batch size is currently one crop or one Debug ROI. GPU speedup therefore depends on image size, model, driver, warm-up, and transfer overhead; never encode assumed timing thresholds as correctness rules.

## Timing and stored results

Each trial records:

- model-load milliseconds;
- inference milliseconds;
- total compute milliseconds;
- whether the pipeline was cached;
- selected device and PaddleOCR version;
- combined text and average recognition confidence;
- every detected line's text, recognition confidence, optional detection confidence, and original-frame half-open rectangle.

The Debug evidence table shows the selected method, ROI or saved image set, device, timing, result, bounds, and OCR confidence or image score.

## Visualization

Review can show the source image beside a clean OCR map or expand the map with `MAP ONLY`. Each recognized text box is drawn with its text, confidence/timing evidence, and coordinates. Wheel zoom is pointer-centered; middle-button drag pans; toolbar controls zoom in, zoom out, or fit. The view is evidence for model comparison and annotation—not an automatic decision surface.

## Failure and privacy boundary

- Reject unsupported model or device names.
- Reject OCR boxes outside their parent crop and crops outside the source image.
- Delete temporary crops after a run when normal cleanup succeeds.
- Do not commit the Python environment, Paddle cache, models, crops, results derived from private frames, or owner datasets.
- Automated tests must not download models or require Paddle, CUDA, or network access.

## Adaptive visual-anchor foundation

**Implemented in Core:** one generic pipeline builds and matches personalized visual anchors without per-element detector classes.

```text
OCR bounds across a short burst
  -> align and normalize crops
  -> measure temporal variation
  -> build grayscale, edge, reliability, and phase assets
  -> bounded steady-state match
  -> OCR fallback when weak or ambiguous
```

`VisualFingerprintBuilder` consumes at least three grayscale frames plus one half-open OCR-owned rectangle per frame. It resamples each rectangle to a bounded canonical size, computes median grayscale and edge templates, derives per-pixel reliability from temporal variance, selects up to four representative animation phases, and classifies the anchor as stable, animated, multi-phase, or OCR-only. The classification is data-derived; there is no Shop, Events, or other element-specific branch.

`VisualAnchorMatcher` searches only a configured translation and scale band around the latest verified bounds. Stable profiles balance grayscale and edges. Animated and multi-phase profiles favor edges and downweight temporally unstable pixels. The matcher reports its component scores, chosen phase, candidate count, and distinct-match margin. It fails closed below threshold, when a separate candidate is too similar, or when the builder classified the crop as OCR-only.

`VisualProfileStore` saves immutable revisions under an explicit caller-owned root. Each revision has an AI-readable `profile.json`, portable grayscale (`.pgm`) assets, and SHA-256 hashes. `current.json` is an atomic pointer; existing revisions are never overwritten. The normative manifest contract is [the visual-anchor schema](../schemas/visual-anchor-profile.schema.json).

`VisualStateRuleEngine` composes anchor ids and a required count declaratively. Mode/state code consumes profile ids and observations instead of adding visual matching algorithms. One observation per anchor is allowed, which keeps evidence distinct and inspectable.

The capture boundary now supports detector-only reads without transferring a complete Roblox frame to CPU. `VisualAnchorRegionMatcher` expands an expected anchor by its configured translation and scale band. Windows validates those client-relative rectangles, packs up to 64 regions whose total requested area is no larger than one client frame into a GPU atlas, reads that atlas once, converts the display-aware sRGB result to deterministic Rec.709 grayscale, and preserves each rectangle's original client coordinates. Full dataset capture remains a separate full-client path.

HDR capture is normalized before either OCR or visual matching. The active DXGI output supplies advanced-color state and peak luminance; DisplayConfig supplies the owner's current SDR-white level. LilacMacro removes Windows' SDR-white boost, tone-maps scene-linear highlights by luminance, compresses out-of-gamut chroma toward that luminance, and tags saved PNGs as sRGB. This avoids making OCR or detector thresholds compensate for a monitor-specific washed-out capture.

Runtime Lab Debug and Wire Test expose `OCR` and `IMAGE + OCR FALLBACK` modes through the same evidence service. Pure OCR mode skips image profiling. Fallback mode first loads the profiles and atomic last-OCR-verified locators for the dataset's current `IMAGE` selections, captures their compact search regions in one GPU atlas, and evaluates only reliable matches with the same state rule used by OCR. Missing optional elements are tolerated when the state rule is already complete; missing required evidence, invalid profiles, weak or ambiguous matches, incomplete state composition, or spatial disagreement runs OCR instead.

After a successful OCR fallback, the shared service takes five compact GPU-region samples over 400 ms at the OCR-owned live bounds, persists immutable profiles and refreshed locators under `%LOCALAPPDATA%\LilacMacro\visual-profiles\wire`, then matches one fresh bounded region. The diagnostic tables show whether image or OCR fallback owned the check, cached/OCR and image bounds, OCR inference time, image-match time, profile-build time, score, strategy, and agreement. Wire Test additionally exposes the median profile, reliability mask, and exact live grayscale crop used for a selected comparison. Deep debug stores those same three compact images as linked evidence. Raw full-client screenshots are not retained by this path.

This diagnostic integration does not authorize clicks, evaluate negative cross-state coverage, or accept profiles for unattended use. Even after an image-first check passes, the following action performs a fresh OCR-owned verification before input. Profile refresh occurs only after an explicit Runtime Lab Debug or Wire Test check reaches a passing OCR fallback.

## Planned personalized detection runtime

**Planned:** connect the implemented adaptive-anchor foundation to OCR as bootstrap and recovery, with cheaper per-user visual detection in steady state.

1. On first use, OCR identifies state text and live button bounds despite the owner's brightness, color, and scale.
2. With owner-visible confirmation, LilacMacro extracts normalized per-user reference crops and records their source state, client size, scale, and OCR provenance. Runtime Lab Debug and Wire Test are the current prototypes for this bootstrap and comparison step.
3. The implemented ROI-atlas capture and bounded matcher perform frequent steady-state checks using those references.
4. Low confidence, conflicting state evidence, a missing target, or a changed layout triggers fresh OCR rather than a blind click.
5. Successful OCR refreshes stale references only through validation and versioned persistence.

The future detector must keep state ownership separate from click authorization: a state may be detected broadly, but input still requires a fresh, bounded target or a scale-relative layout whose anchors were just verified. UI changes that preserve text can recover through OCR; changed semantics, missing anchors, or ambiguous matches must stop safely.

Dataset selection, profile acceptance thresholds, negative coverage, refresh cadence, and low-end-device budgets remain **Unresolved**. Search padding is derived generically from matcher translation and scale options rather than per-element code. No YOLO runtime is implemented today, and adaptive-anchor matches do not yet authorize macro input.
