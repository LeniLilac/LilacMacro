# Documentation index

**Status: Current.** This is the canonical entrypoint for owner and coding-agent documentation.

## Start here

| Document | Authority |
|---|---|
| [Project status](PROJECT-STATUS.md) | Implemented, Prototype, Planned, and Unresolved boundaries |
| [Contributing](../CONTRIBUTING.md) | Required change and validation workflow |
| [Development](DEVELOPMENT.md) | Repository layout, setup, conventions, persistence, and file limits |
| [Testing](TESTING.md) | Automated commands, risk-based coverage, and owner-only live testing |
| [Troubleshooting](TROUBLESHOOTING.md) | Local SDK, OCR, capture, hotkey, dataset, and log problems |
| [Deep debug](DEEP-DEBUG.md) | Diagnostic archive contract, retention, privacy, and agent inspection |

## Product and design

| Document | Authority |
|---|---|
| [README](../README.md) | Concise project entrypoint and quick start |
| [Product](../PRODUCT.md) | User, purpose, product principles, and product boundary |
| [Design](../DESIGN.md) | Current macro-shell and Setup UI design |
| [Privacy](../PRIVACY.md) | Current local data handling and planned secret protection |

## Engineering

| Document | Authority |
|---|---|
| [Architecture](ARCHITECTURE.md) | Current project layers, owners, and integration boundaries |
| [Game behavior](GAME-BEHAVIOR.md) | Field-observed Debug OCR state and action ledger |
| [OCR and vision](OCR-AND-VISION.md) | Current PaddleOCR pipeline and planned hybrid detection |
| [Placement authoring](PLACEMENT-AUTHORING.md) | Current Setup data model and authoring/playback boundary |
| [Macro architecture](MACRO-ARCHITECTURE.md) | Planned priority scheduler, Lobby reset, modules, and mode flows |

## Dataset contract

| Document | Authority |
|---|---|
| [Dataset format](DATASET-FORMAT.md) | Manifest, image, coordinate, annotation, and OCR-trial contract |
| [Agent dataset workflow](AGENT-DATASET-WORKFLOW.md) | Bounded and privacy-safe dataset inspection |
| [Dataset schema](../schemas/dataset.schema.json) | Normative machine-readable dataset schema |

## Agent instructions

The root [AGENTS.md](../AGENTS.md) routes repository-wide work. More specific rules apply automatically within their directories:

- [App agent instructions](../src/LilacMacro.App/AGENTS.md)
- [Core agent instructions](../src/LilacMacro.Core/AGENTS.md)
- [Windows agent instructions](../src/LilacMacro.Windows/AGENTS.md)
- [Tools agent instructions](../tools/AGENTS.md)
- [Test agent instructions](../tests/AGENTS.md)

## Authority and status

- Runtime code and schemas define what the current build accepts.
- [Game behavior](GAME-BEHAVIOR.md) is the canonical ledger for field-observed state rules; do not duplicate it in the README.
- [Project status](PROJECT-STATUS.md) decides whether a capability is Implemented, Prototype, Planned, or Unresolved.
- Planned documents are design contracts, not claims that runtime code exists.

Release, support, code-of-conduct, public security-reporting, and changelog documents are intentionally deferred until the project has corresponding workflows.
