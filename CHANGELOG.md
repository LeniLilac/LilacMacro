# Changelog

## Unreleased

- Bundled Python 3.12, the CPU OCR runtime, and the supported OCR model pairs in the Windows installer.
- Added a consent-gated GPU OCR setup screen with streamed progress and logs; GPU packages install per user and never replace the bundled CPU runtime.
- Removed the separate Python installer fallback that could show an unexpected Python UAC prompt.

## 1.0.144 — Public beta

- Added a scale-relative DPS image fast path for phantom placement evidence with OCR fallback.
- Bounded failed placement selection to three reselection attempts and continued with the next authored step after exhaustion.

## 1.0.143 — Public beta

- Fixed first-run OCR setup when the Windows Python launcher exists without Python 3.12, allowing the automatic App Installer fallback to run.
- Added opt-in, rate-limited OCR setup failure telemetry using classified codes and bounded runtime flags without uploading setup output.
- Added opt-in local-instance setup, repair, add, remove, and open failure telemetry using bounded operation/status fields without uploading account or exception details.
- Hid the manual diagnostic upload progress track until manual uploads are enabled.

## 1.0.142 — Public beta

- Made the main Macro shell bootstrap OCR on first launch, including automatic Python 3.12 setup when needed.

## 1.0.141 — Public beta

- Added optional current Roblox client PNG attachments to real Discord runtime events through Components V2 Media Gallery messages.
- Kept the Test webhook action text-only and made screenshot capture or upload failures non-blocking for automation.
- Updated the Discord webhook request contract to enable Components V2 attachments and wait for server confirmation.
- Incremented the privacy notice for the new Discord screenshot behavior.

## 1.0.140 — Public beta

- Published the first source-available Windows public beta and project-signed six-asset release channel.
- Added separate Story map and act selectors, including Infinite and Mastery.
- Added bounded Story Infinite wave detection and verified in-match reset at a configured wave.
- Moved Plan and placement sharing into a symmetric in-page modal.
- Made local Deep Debug archive cleanup independent from automatic and manual uploads.
- Added current Story, Expedition, Event, utility, docking, Discord-event, update, privacy, telemetry, and diagnostic work accumulated during private testing.

The beta still requires supervised owner acceptance for live game behavior and multi-session lifecycle changes.
