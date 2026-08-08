# Agent dataset workflow

**Status: Implemented local workflow.** Read the data contract in [Dataset format](DATASET-FORMAT.md) and the repository privacy rules in [PRIVACY.md](../PRIVACY.md) before inspecting owner data.

Dataset images can contain personal or third-party game content. Keep them outside the repository, do not upload them, and inspect only the dataset the owner places in scope.

## Build a bounded review view

Run:

```powershell
./scripts/New-AgentDatasetView.ps1 "C:\path\to\dataset"
```

The command first validates every declared image, SHA-256 digest, dimension, annotation bound, and manifest invariant. It then creates a new timestamped `.agent-view` directory inside that dataset. Existing views are never overwritten.

## Inspection order

1. Read `summary.md` for scope, privacy, capture mode, dimensions, and counts.
2. Read `agent-index.json` and line-delimited `frames.jsonl` for machine-friendly paths and metadata.
3. Inspect the chronological `contact-sheets/contact-sheet-*.png` files before opening individual frames.
4. Filter `frames.jsonl` OCR trial regions by `is_ocr_evidence` or `is_visual_anchor` before inspecting unrelated detected text. Read `evidence_role`, `spatial_selector`, its override flag, and optional anchor text as explicit owner-authored policy.
5. Inspect an exact image under `images/` only when a contact sheet shows a transition or ambiguity worth expanding.
6. Inspect `crops/` for labeled regions and `ocr-maps/` for source-to-text comparisons. A crop path and every nested OCR line rectangle are joined to their annotation in `frames.jsonl`.

OCR child rectangles use the same original-frame coordinate space as their manual parent rectangle. `global_group_id` joins the same coarse region across frames without conflating frame-specific OCR results. `minimum_pool_matches` plus child `required`/`pool` roles describes the evidence threshold without state-specific code. Each trial includes its `device`; `total_compute_milliseconds` includes model load plus inference, and `model_was_cached` distinguishes a resident model/device pipeline from a cold run. OCR output is advisory and does not establish UI ownership on its own.

## Validate without rendering

```powershell
dotnet run --project tools/LilacMacro.DatasetTool -- validate "C:\path\to\dataset"
```

The validator is read-only. Agent-view generation writes only under the explicit dataset path or explicit output directory.
