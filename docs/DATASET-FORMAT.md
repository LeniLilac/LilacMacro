# Dataset format

**Status: Implemented schema version 1.** The normative schema is [`schemas/dataset.schema.json`](../schemas/dataset.schema.json); agent inspection steps are in [Agent dataset workflow](AGENT-DATASET-WORKFLOW.md).

`dataset.json` is UTF-8 JSON with snake-case property names. Schema version 1 records:

- dataset identity, name, notes, creation time, and draft/final state;
- capture mode plus requested frame count and duration;
- source Roblox process and actual client dimensions;
- one entry per PNG with capture time, SHA-256 digest, frame verdict, notes, and annotations;
- each annotation's stable ID, optional global-group ID, label, note, integer pixel rectangle, evidence-pool minimum, and zero or more OCR trials;
- each OCR trial's allowlisted detector/recognizer pair, execution device (`cpu` or `gpu:0`), combined text, average recognition confidence, model-load time, inference time, cache state, PaddleOCR runtime version, and UTC timestamp;
- each detected text line's original-frame half-open rectangle, text, recognition confidence, detector confidence when Paddle exposes it, OCR/image relevance, evidence role, match mode, and spatial selector.

Rectangles are half-open client-image coordinates: `x` and `y` are inclusive, while `width` and `height` describe the covered extent. Manual rectangles must fit inside their frame; OCR text rectangles must also fit inside their manual parent rectangle. Drags narrower or shorter than three pixels are ignored.

The owner still draws the parent OCR search area manually. A local annotation belongs to one frame. Enabling `GLOBAL` creates one linked member with the same bounds and shared label, note, and pool minimum on every frame; later manual captures inherit those linked coarse regions before the manifest is saved. Each member retains its own frame-specific OCR trials. Turning `GLOBAL` off collapses the group back to the selected frame by removing its propagated members and keeping the selected annotation as local. Deleting a still-global annotation removes every linked member.

Inside a stored trial, `is_ocr_evidence` marks a detected text line that may contribute to OCR state rules and `is_visual_anchor` marks a line whose exact bounds may contribute samples to a personalized visual profile. `evidence_role` is `required`, `pool`, or `none`. The annotation's `minimum_pool_matches` expresses `all required + N distinct pool phrases`; phrases are compared after the standard alphanumeric normalization, and the same phrase cannot occupy both groups. This is the generic representation for rules such as `Teams + 1 of {Unequip, Unequip All, Quick, Quick Sell}`.

`match_mode` is `exact` by default or `fuzzy_phrase` for owner-selected long text. `spatial_selector` is `any`, `leftmost`, `rightmost`, `topmost`, `bottommost`, `same_row`, or `nearest_anchor`. Review infers an extreme selector when duplicate normalized candidates are spatially separated inside the coarse region. `spatial_selector_overridden` records an owner choice; relational selectors also store `spatial_anchor_text`. Missing fields retain their defaults when opening older version 1 manifests. Selecting a detected line never creates or changes the manually drawn parent rectangle.

`capture_mode` is `timed` or `manual`. Timed manifests store their requested target count and duration. Manual manifests store both requested fields as `0`; their actual frame count is the length of `frames`. Version 1 manifests created before this field existed are interpreted as `timed`.

Manifests are written to a sibling temporary file and atomically replaced. Images are encoded to a temporary filename before being moved into `images`.

Draft directories begin with `.draft-`. Finalization validates the dataset name, chooses a collision-free slugged directory name, moves the complete draft, and then marks the manifest finalized. An interrupted draft remains discoverable and reviewable.

Every manifest declares the `lilacmacro.dataset` format, its schema URL, `images` relative root, and `roblox_client_pixels_half_open` coordinate space. The normative JSON Schema is [schemas/dataset.schema.json](../schemas/dataset.schema.json).

`LilacMacro.DatasetTool` validates image hashes, dimensions, manual rectangles, and nested OCR rectangles, then can generate a bounded agent view containing chronological contact sheets, per-annotation crops, multi-box OCR maps, `frames.jsonl`, `agent-index.json`, and a human-readable `summary.md`. Generated views live under the dataset's ignored `.agent-view` directory unless an explicit empty output directory is supplied. Existing views and dataset content are never overwritten.
