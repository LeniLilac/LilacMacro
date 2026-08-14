# Runtime evidence ownership

**Status: Implemented.** This document defines how a runtime search area is traced to owner-authored dataset evidence and how that evidence is shipped with LilacMacro.

## Contract

Every semantic search area used to decide a Roblox UI state must have one named annotation in a dataset. Runtime code must not invent a broader crop, silently use the first annotation, or infer an unclear annotation's intent.

- A `DebugStateSpec` names its exact annotation with `RegionLabel`.
- A static production search rectangle is registered in `RuntimeSearchRegionEvidenceCatalog` with its code owner, bundled dataset, frame, annotation label, bounds, and intent.
- Runtime fields consume the catalog rectangle; they do not repeat numeric rectangles at the call site.
- Dynamic search areas must be derived from fresh bounds owned by a named dataset state. Full-client capture is only a capture carrier, not semantic evidence ownership.
- Action geometry, such as a click derived inside a verified control, is not itself a search area. Its authorizing state still needs dataset-owned evidence.

The repository policy rejects missing labels, duplicate matching labels, unregistered static search rectangles, catalog-to-dataset geometry drift, missing bundle entries, extra bundle entries, and invalid image hashes.

## Bundled evidence

`eng/runtime-evidence.json` is the allowlist for the curated public evidence bundle. `scripts/Sync-RuntimeEvidence.ps1` copies only the listed source frames and annotations into `src/LilacMacro.App/Assets/RuntimeEvidence`. App builds and publishes include that directory, so desktop and runner sessions use the same evidence without access to the owner's Documents folder.

The ordinary rule remains that owner datasets and captures stay local. The only repository exception is the explicitly reviewed, allowlisted runtime-evidence slice. Adding a source directory to the specification does not authorize copying the full source dataset.

After changing the specification or an allowed source dataset, run:

```powershell
./scripts/Sync-RuntimeEvidence.ps1
./scripts/Test-RuntimeEvidence.ps1
./scripts/Test-RepositoryPolicy.ps1
```

## Required workflow

1. Locate the owner dataset and read its notes before editing runtime detection.
2. Identify the exact state owner and exact annotation label for each search area.
3. If the state, layer, search-versus-action role, UI-scale coverage, or positive/negative evidence is unclear, stop and ask the owner. Do not guess.
4. Add the minimum required frame and annotation to `eng/runtime-evidence.json` and regenerate the bundle.
5. Add or update the `DebugStateSpec` or `RuntimeSearchRegionEvidenceCatalog` entry.
6. Route production code through that owner and add positive, negative, missing-label, and ambiguity coverage as appropriate.
7. Run the evidence policy, full validation, and the owner-only live checklist required by [Testing](TESTING.md).

## Review questions

Before approving a new state, answer all of these from the dataset notes or ask the owner:

- What exact UI state does the box own?
- Is it a search region, an action target, or both through separate verified steps?
- Which other visible layer can overlap it?
- Which UI scales and positive/negative states are represented?
- What fresh destination evidence proves the preceding action succeeded?
