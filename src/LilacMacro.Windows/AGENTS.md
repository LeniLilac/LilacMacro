# Windows agent instructions

This file applies to `src/LilacMacro.Windows`.

- Use ordinary Windows APIs only. Never inject, inspect process memory, hook Roblox, or add anti-cheat bypass behavior.
- Windows owns Roblox window discovery, client sizing, display geometry, Windows Graphics Capture, hotkeys, and input delivery.
- Treat all requested sizes and action points as client-area pixels. Convert to screen coordinates only from freshly observed client bounds.
- Revalidate the target window, process identity, client size, and focus after delays or external transitions before input.
- Preserve fresh-frame capture semantics. Reject resized or stale surfaces instead of cropping or clicking from assumed geometry.
- Route mouse and keyboard operations through the shared Roblox-compatible protocol and its single operation gate; do not add direct cursor-teleport click paths.
- Keep input bounded and cancellation-aware. Release held keys and mouse buttons, restore the cursor when promised, and balance temporary Shift Lock on every exit path.
- Native handles and unmanaged resources require deterministic disposal. Check native return values and surface actionable failures.
- Keep Win32 details out of Core and WPF details out of Windows.
- Unit tests must exercise pure descriptors/protocol policies without requiring a live Roblox process. Live behavior is owner-tested, never computer-controlled by agents.
