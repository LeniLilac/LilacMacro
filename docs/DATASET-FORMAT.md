# Dataset format

`dataset.json` is UTF-8 JSON with snake-case property names. Schema version 1 records:

- dataset identity, name, notes, creation time, and draft/final state;
- capture mode plus requested frame count and duration;
- source Roblox process and actual client dimensions;
- one entry per PNG with capture time, SHA-256 digest, frame verdict, notes, and annotations;
- each annotation's stable ID, label, note, integer pixel rectangle, and zero or more OCR trials;
- each OCR trial's allowlisted detector/recognizer pair, execution device (`cpu` or `gpu:0`), combined text, average recognition confidence, model-load time, inference time, cache state, PaddleOCR runtime version, and UTC timestamp;
- each detected text line's original-frame half-open rectangle, text, recognition confidence, and detector confidence when Paddle exposes it.

Rectangles are half-open client-image coordinates: `x` and `y` are inclusive, while `width` and `height` describe the covered extent. Manual rectangles must fit inside their frame; OCR text rectangles must also fit inside their manual parent rectangle. Drags narrower or shorter than three pixels are ignored.

`capture_mode` is `timed` or `manual`. Timed manifests store their requested target count and duration. Manual manifests store both requested fields as `0`; their actual frame count is the length of `frames`. Version 1 manifests created before this field existed are interpreted as `timed`.

Manifests are written to a sibling temporary file and atomically replaced. Images are encoded to a temporary filename before being moved into `images`.

Draft directories begin with `.draft-`. Finalization validates the dataset name, chooses a collision-free slugged directory name, moves the complete draft, and then marks the manifest finalized. An interrupted draft remains discoverable and reviewable.

Every manifest declares the `lilacmacro.dataset` format, its schema URL, `images` relative root, and `roblox_client_pixels_half_open` coordinate space. The normative JSON Schema is [schemas/dataset.schema.json](../schemas/dataset.schema.json).

`LilacMacro.DatasetTool` validates image hashes, dimensions, manual rectangles, and nested OCR rectangles, then can generate a bounded agent view containing chronological contact sheets, per-annotation crops, multi-box OCR maps, `frames.jsonl`, `agent-index.json`, and a human-readable `summary.md`. Generated views live under the dataset's ignored `.agent-view` directory unless an explicit empty output directory is supplied.
