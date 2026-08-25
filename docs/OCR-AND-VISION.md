# OCR and vision

**Status: Current OCR implementation, Implemented adaptive-anchor foundation, and Planned runtime integration.**

## Current OCR pipeline

Official installers bundle a read-only Python 3.12 CPU runtime, PaddlePaddle 3.3.0, PaddleOCR 3.7.0, and the two supported model pairs. The bundled CPU worker explicitly disables Paddle's MKL-DNN/oneDNN acceleration because the bundled CPU build can reject a PP-OCRv6 PIR attribute during detection; GPU workers retain their accelerated path. The bundled CPU runtime is used directly from the install directory; per-user state and model cache remain under `%LOCALAPPDATA%\LilacMacro\ocr`. After the first-run privacy choices are saved, a supported NVIDIA GPU can trigger a separate visible setup screen that installs only its matching GPU runtime into a per-user environment. Managed runner desktops perform the same check against their own Windows profile even when they use an already-accepted shared configuration, so an owner's GPU environment never masks a missing runner environment. The setup script no longer installs Python through Windows App Installer and does not install NVIDIA drivers. Development machines without the bundle may still use the script's existing Python 3.12 discovery path. It installs one user-selected GPU runtime at a time:

```powershell
./scripts/Setup-Ocr.ps1 -Device cpu
./scripts/Setup-Ocr.ps1 -Device gpu
```

GPU setup queries NVIDIA GPU 0 through `nvidia-smi`, rejects hardware older than compute capability 6.0, and selects the official package by architecture: CUDA 11.8 for Pascal and Volta; CUDA 12.6 for Turing, Ampere, and Ada; CUDA 12.9 for Hopper and Blackwell. Setup verifies that Paddle sees a CUDA device and records the detected model, generation, capability, driver, and package feed in `gpu\runtime-profile.json`. CPU and GPU environments are separate, so GPU setup never mutates the read-only bundled CPU runtime. AMD and Intel graphics use CPU OCR.

The 3.3.0 GPU runtime has completed tensor and PP-OCRv6 small inference smoke tests on a hosted GTX 1080 Ti (Pascal, compute capability 6.1), a hosted Titan RTX (Turing), a Windows RTX 3070 (Ampere), a hosted RTX 4060 (Ada), and a hosted RTX 5060 (Blackwell). The Pascal qualification used the official CUDA 11.8 package path and recognized the synthetic `LILAC GPU OCR 12345` probe correctly. Each installed machine still performs the same setup-time device and import verification before its GPU runtime is accepted; untested GTX 10 models remain guarded by that device-local probe.

Macro Settings exposes three persisted runtime modes. `Auto` uses a ready GPU and otherwise the bundled CPU runtime; `GPU` requires the per-profile NVIDIA runtime; `CPU` always uses the bundle. Managed-runner snapshots preserve whether GPU is allowed. `TEST OCR` generates a deterministic high-contrast `TEST` card locally and requires the selected runtime to recognize it. `REPAIR OCR` rebuilds the selected runtime or restores the bundled model cache; neither action sends the test image or OCR result over the network.

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

Story Infinite uses a compact counter-specific hybrid owner rather than a generic text state. OCR may parse a merged `140wave` region or separate number and `wave` boxes only inside the bundled `(561,0,110,52)` client ROI. The same pixels must independently contain the counter's bright neutral glyph mass, dark capsule field, and blue icon band, and the reset threshold requires two fresh nondecreasing readings. Text on an unrelated background cannot authorize Restart.

Startup UI-scale normalization deliberately does not OCR the displayed numeric scale. OCR verifies the Settings/search/navigation structure and the semantic `Miscellaneous`/`UI Scale` row before input. Independent RGB panel geometry measures the rendered result, because the same numeric input may render differently across devices and Windows sessions.

## CPU, GPU, and resident workers

- The selected device is stored with each OCR trial as `cpu` or `gpu:0`.
- One-shot mode starts a helper process for a run and exits afterward.
- `KEEP LOADED` starts one persistent helper and caches detector/recognizer pipelines by model and device for the app session.
- Dataset Builder and Runtime Lab default to GPU with `KEEP LOADED` enabled. When the GPU runtime is ready, each tool preloads the PP-OCRv6 small detector/recognizer pair as it opens; a missing GPU runtime remains an explicit setup state and never triggers an automatic install or CPU fallback.
- Requests and responses use a unique temporary channel, worker-exit detection, bounded JSON, and cleanup. The worker publishes fixed lifecycle phases for input validation, crop preparation, model loading, inference, and response writing so Deep Debug can distinguish the stage of a stall without recording input paths. Cold model loading has a separate two-minute bound; every other phase retains the 30-second watchdog. Readers share status/response files with atomic replacement, and workers ignore inherited Python runtime paths and user-site packages.
- A user-started Macro run preloads its preferred resident worker before Roblox is restarted. A recoverable GPU worker failure receives one fresh-worker retry; a second consecutive failure switches that run to the bundled CPU worker. The next run begins GPU-eligible again, so one transient fault never permanently changes the configured device preference. Dataset Builder and owner-triggered Runtime Lab trials retain their explicit device behavior.
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

## Expedition reward-strip evidence

**Prototype runtime:** the Expedition optimizer uses one scale-derived compact reward-strip ROI rather than full-client OCR. The audited three-difficulty corpus contains 2,924 independent pool frames. Four-times enlargement, spacing-derived card ownership, and bounded fuzzy labels recover the five optimization resources without letting neighboring-card text authorize a value. Raw full-client detection missed most small labels, so it is not an accepted production path.

Quantity correction is resource- and card-contextual. The stylized `1x` glyph is read as `bx` for Equipment Lock/Reroll, and the post-update Difficulty 3 run established the bounded trailing-`1` forms `31x -> 3bx/31bx`, `41x -> 4bx/4kx/41bx`, and `51x -> 5bx/5kx/51bx` for Fuel Cell, Equipment Scrap, and Expedition Coin. Equipment Lock also exposes `11x -> 1bx`. Ambiguous cross-resource forms such as Expedition Coin `2bx/2kx` remain unreadable without independent evidence. A detected card with an unresolved quantity is retried rather than recorded as zero, and an incomplete target-only observation is never persisted as a five-resource pool. The complete evidence, parser contract, economics, and blocked validation work are in [Expedition reward optimization](EXPEDITION-REWARD-OPTIMIZATION.md).

## Expedition current-node evidence

**Planned runtime:** locate and hover only the current progress marker, require stable tooltip structure, and OCR its revealed semantic title. Use that verified title to learn a per-desktop or per-runner color profile from multiple fresh bar samples. Color is the hot path only when one learned profile wins with a safe margin across consecutive captures; any miss, ambiguity, environment mismatch, or OCR contradiction falls back to hover OCR and refreshes calibration. Future-node lookahead and global node-color constants are not part of the design. The dataset evidence, cache scope, and required negative coverage are in [Expedition runtime](EXPEDITION-RUNTIME.md).

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

After a successful OCR fallback, the shared service takes five compact GPU-region samples over 400 ms at the OCR-owned live bounds, persists immutable profiles and a round-trip-tested refreshed locator under `%LOCALAPPDATA%\LilacMacro\visual-profiles\wire`, then matches one fresh bounded region. A missing or invalid locator keeps image evidence unavailable and falls back to OCR; it never silently substitutes static coordinates. The diagnostic tables show whether image or OCR fallback owned the check, cached/OCR and image bounds, OCR inference time, image-match time, profile-build time, score, strategy, and agreement. Wire Test additionally exposes the median profile, reliability mask, and exact live grayscale crop used for a selected comparison. Deep debug stores those same three compact images as linked evidence. Raw full-client screenshots are not retained by this path.

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
