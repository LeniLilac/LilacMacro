# Tools agent instructions

This file applies to `tools` and repository dataset-tool behavior.

- Dataset inspection is bounded, local, and validation-first. Validate manifests, hashes, dimensions, and nested coordinates before rendering derived views.
- Read only the dataset explicitly placed in scope. Never scan broad personal directories for captures.
- Treat every frame, crop, OCR map, manifest, and summary as potentially private or third-party content.
- Default generated views to the dataset-local ignored `.agent-view` directory. An explicit output directory must be missing or empty.
- Never overwrite an existing dataset, agent view, image, or report.
- Keep output paths machine-readable and relative inside indexes; preserve chronological ordering and stable identifiers.
- Tools may consume Core contracts but must not own application workflow, Windows input, or WPF lifecycle.
- Commands must be noninteractive, cancellation-aware where practical, and return a nonzero exit code on validation failure.
- Tests and examples must use synthetic, privacy-safe data rather than owner captures.
