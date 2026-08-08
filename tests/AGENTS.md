# Test agent instructions

This file applies to `tests`.

- Tests must be deterministic and independent of live Roblox, desktop focus, display scale, GPU availability, Paddle downloads, and network access.
- Prefer pure unit tests for Core policies and protocol descriptors. Use temporary directories for persistence tests and clean them in `finally` or disposal.
- Use synthetic manifests, pixels, OCR boxes, paths, and input descriptors. Never copy owner captures, usernames, server links, webhook URLs, or logs into fixtures.
- Regression tests should reproduce the exact positive case plus nearby negative and boundary cases that must remain fail-closed.
- Assert coordinate-space behavior explicitly, including half-open bounds and image/client dimensions.
- Do not loosen assertions or add broad retries to hide nondeterminism.
- Keep each test focused on one behavioral contract and name it for the observed outcome.
- Live UI and Roblox validation belongs to the owner. Document a manual checklist rather than automating either through computer-control tooling.
