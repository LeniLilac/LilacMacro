# TermWrap v0.6 payload

This directory contains the pinned x64 `TermWrap.dll` and its required
`Zydis.dll` decoder dependency from the upstream v0.6 release. LilacMacro never
downloads or updates these native files at runtime. The archive and file hashes
are recorded in `payload.json` and are verified before setup, repair, or
migration changes Windows.

`UmWrap.dll` and `EndpWrap.dll` are intentionally excluded because the local
runner disables device, drive, clipboard, microphone, printer, and smart-card
redirection. `Zydis.dll` cannot be excluded: `TermWrap.dll` imports its decoder
API for the compatibility scanner used by both preflight and the wrapped
service.

TermWrap is an experimental third-party compatibility component and is not a
Microsoft-supported Windows client configuration. Before any system mutation,
LilacMacro invokes TermWrap's published `ServiceMain` export in a sacrificial
`rundll32.exe` process under a debugger. TermWrap's bundled offset scanner
analyzes the local `termsrv.dll` in that process only; every required patch must
be found. Successful evidence is cached by the probe version and both exact
binary hashes, and is invalidated automatically by Windows, payload, or probe
changes. LilacMacro restores the original TermService state when the optional
local runner is removed.

TermWrap is licensed under the MIT license in `LICENSE.txt`. Zydis is licensed
under the MIT license in `ZYDIS-LICENSE.txt`.
