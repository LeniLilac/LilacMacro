# Security policy

## Supported version

Only the newest GitHub public-beta release is supported with security fixes. Older builds may contain known issues and should be upgraded before testing a report.

## Report a vulnerability privately

Email [lilithlilac000@gmail.com](mailto:lilithlilac000@gmail.com) with the subject `LilacMacro security report`. Do not open a public issue for a vulnerability, credential, private-server link, webhook URL, diagnostic archive, screenshot, local path, or exploit details.

Include the affected version, the smallest safe reproduction, expected impact, and whether the issue is already public. Do not attach a Deep Debug archive or other private capture until requested. Never test against another person's account, device, data, or hosted service traffic.

The project will acknowledge reports on a best-effort basis, investigate without promising a fixed timeline, and coordinate disclosure when a fix is available. Please allow reasonable remediation time before publishing details.

## Security boundary

LilacMacro uses ordinary Windows capture and input. It does not inject into Roblox, read or modify Roblox process memory, hook the game, or bypass anti-cheat. Remote services may narrow or pause documented behavior but cannot deliver executable automation, coordinates, scripts, or arbitrary commands.

Official installers are published only at [GitHub Releases](https://github.com/LeniLilac/LilacMacro/releases). They are intentionally not Authenticode-signed, so Windows reports an unknown publisher. Each official release carries GitHub SHA-256 asset digests, a checksum file, and an Ed25519 project-signed manifest. See [Installer trust](docs/INSTALLER.md).
