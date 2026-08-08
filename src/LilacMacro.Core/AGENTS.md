# Core agent instructions

This file applies to `src/LilacMacro.Core`.

- Core must remain platform-independent: no WPF, Win32, Direct3D, Windows-only paths, process control, or UI dependencies.
- Put deterministic validation, normalization, schedules, geometry, dataset contracts, OCR rules, and placement policies here.
- Use immutable records or narrowly mutable persistence models; keep side effects behind explicit stores or contracts.
- Pixel rectangles are half-open original-image coordinates. Validate dimensions, clamp only at explicit UI boundaries, and reject invalid persisted geometry.
- State and selection policies must be deterministic, bounded, inspectable, and fail closed when required evidence is missing or ambiguous.
- Static or derived coordinates never authorize live input; Core may calculate candidates, while the caller must revalidate live state.
- Persist JSON atomically through a sibling temporary file and replacement. Validate before writing and never partially update a document in place.
- Preserve schema versions and backwards-compatible reads unless a deliberate migration is documented and tested.
- Keep user-specific paths and secrets out of Core defaults and test fixtures.
- Every policy change needs positive, negative, boundary, and malformed-input coverage appropriate to its risk.
