# Optional local runner session

**Status: Prototype and unavailable by default.** Provisioning, isolation, IPC, typed snapshots, runner profile policy, cleanup, installer, pinned TermWrap v0.6 payload, exact-binary native preflight, and the shared windowless Story/Raid/Challenge runtime are implemented. Distribution remains blocked on release signing, disposable-VM lifecycle certification, and owner live acceptance.

## Boundary

The optional local runner creates one standard `LilacMacroRunner` Windows account and one visibly connected loopback RDP session. It never changes the owner's profile and never grants administrator membership to the runner. TermWrap enables an experimental concurrent client session outside Microsoft's supported Windows client configuration; setup is therefore opt-in, exact-binary probed, and fail-closed.

LilacMacro uses ordinary Windows capture and input inside the runner session. The desktop controller sends declarative commands and immutable configuration snapshots only. It never forwards raw mouse or keyboard input across sessions.

## Components

| Component | Owner |
|---|---|
| `LilacMacro.SessionSetup.exe` | Signed elevated helper with only `install`, `repair`, `remove`, and `uninstall-cleanup` verbs |
| `LilacMacro.SessionWorker.exe` | Windowless runner-session process and named-pipe server |
| `LocalSessionProvisioner` | Idempotent account, profile, TermService, firewall, task, rollback, and cleanup coordination |
| `RunnerProfilePolicy` | Versioned, runner-only package and promotion allowlists |
| `RunnerRuntimeSnapshot` | Immutable plans, placement setups, keybindings, model choice, and non-secret settings |
| Session pipe contracts | Versioned handshake, snapshot selection, start/stop, events, heartbeat, and cancellation |

Persistent machine-owned state lives below `%ProgramData%\LilacMacro`. The provisioning journal records resource ownership, original system values, versions, hashes, and SIDs. It never stores the generated password. The password is stored as a local-machine Windows Credential Manager credential.

## Preflight and provisioning

Setup verifies the bundled payload hashes, including TermWrap's required x64 Zydis decoder, plus the local TermService hash, existing RDP state, remote-session state, worker binary, and elevation before mutation. It then starts a hidden sacrificial `rundll32.exe` under the Windows debugging API and invokes TermWrap v0.6's published `ServiceMain` export. The debugger loop remains on the dedicated thread that created the probe process, as required by Windows. TermWrap's own offset scanner analyzes the local `termsrv.dll`; it must find every patch needed by the runner. The sacrificial process never changes TermService, registry, firewall, accounts, or the installed DLL. Probe implementation revisions invalidate prior cached evidence as well as binary hash changes.

A successful result is stored in `%ProgramData%\LilacMacro\Session\compatibility-cache.json` and bound to the probe version, architecture, OS build, exact TermService SHA-256, and exact TermWrap SHA-256. A Windows update or native-payload change invalidates the cache automatically and forces a new offline probe. Failed evidence is not cached. Disabled device-redirection patch diagnostics are advisory because the runner explicitly disables those channels; all session-enabling patch failures are blocking.

Provisioning then:

1. captures original registry state in the journal;
2. creates the non-administrator runner and adds only Remote Desktop Users membership;
3. applies ACLs for the owner SID, runner SID, SYSTEM, and Administrators, then stores the loopback RDP secret as a generic `TERMSRV` credential rather than using the domain-password target grammar;
4. performs one controlled runner logon and verifies the profile-policy receipt;
5. applies the pinned TermWrap and loopback RDP configuration;
6. installs exact enabled all-profile TCP/UDP inbound block rules for the owned port covering every IPv4 and IPv6 remote address except the authorized `127.0.0.1` endpoint, verifies their full semantics through Windows Firewall policy, and waits up to 15 seconds for loopback reachability after TermService restarts; same-host connections to a non-loopback local address are not treated as external-firewall evidence because Windows can route them through the local loopback fast path;
7. registers the app-owned logon task for the worker with the machine-qualified local runner identity required by current Windows 11 Task Scheduler XML;
8. starts the worker and validates a fresh WGC frame in the visible session;
9. validates fresh WGC capture and the shared WPF-free workflow host, then records Ready only when the complete runtime health check passes.

Any failure runs the rollback journal. Cleanup attempts every independently owned resource even when an earlier cleanup step fails, treats an already-absent scheduled task as successful idempotent cleanup, reports each real failed step, and restarts TermService only when journal evidence or observed registry drift proves that setup reached the machine-configuration boundary. Restart stops the active Windows 11 `UmRdpService` dependency before `TermService`, then restores both running services. Service-control rejection is reported immediately, while pending start/stop transitions follow the service wait hint on a bounded 60-second deadline. If fresh service evidence shows that Windows returned TermService to stable Running after accepting its stop and currently advertises STOP control support, the helper may issue at most three total stop requests inside that same deadline. Win32 1051 at that boundary authorizes only a bounded re-stop of the known `UmRdpService` dependent before retrying TermService; other states, services, and errors never authorize dependent control. A not-yet-controllable service remains observation-only. A terminal failure reports the current named state, accepted request count, checkpoint, wait hint, and Win32 exit code. If rollback is incomplete, state becomes Recovery Required and the journal remains for repair or cleanup. Repair clears an orphaned journal when live inspection proves that rollback already removed every owned resource and restored every recorded value.
Failures before the first journal write are recorded as non-mutating setup failures, and the elevated helper persists its final problem before exiting. On application restart, Settings reconciles an orphaned Installing or Removing state against the journal and live helper process, so an interrupted operation cannot remain indefinitely stale.
Runner profile access failures name the exact runner-hive key and value that Windows rejected, while retaining the bounded failure code, so disposable-VM certification can distinguish a platform policy restriction from a generic setup failure.

## Runner profile policy

The policy retains an exact per-user package allowlist for future compatibility work, but package removal is not part of the required controlled setup pass. Fresh Windows profiles can register AppX packages asynchronously, so making debloat a provisioning gate is unreliable and unrelated to runtime isolation. The required policy disables only runner-profile promotion, suggestions, notifications, OneDrive/Teams/Office startup, and consumer content through ordinary runner-hive registry values. The Windows 11 `TaskbarDa` Widgets toggle and the per-user `Software\Policies` OneDrive value are intentionally not provisioning requirements because current Windows builds can deny those cosmetic/non-isolation values even while the rest of the profile is writable. OneDrive startup suppression remains required through the runner's ordinary `Run` key.

The policy rejects wildcards, all-user/provisioned package mutation, machine service policy, Defender changes, Windows Update changes, registry cleaners, and owner-profile writes. Explorer, DWM, Defender, Firewall, Windows Update, Store infrastructure, WebView2, GPU, audio, networking, WGC, and gaming services remain.

## Runtime and failure behavior

The named pipe validates protocol version, worker/app version, server executable, server SID, owner SID, runner SID, and snapshot revision. A lost pipe cancels the active worker run and releases ownership. Start requires both a freshly validated WGC frame and the shared headless workflow host; hidden, minimized, disconnected, stale, black, or runtime-less operation must stop rather than authorize input.

The worker materializes the selected immutable snapshot below its app-owned ProgramData runtime root, validates placements, state contexts, keybindings, OCR model, app version, owner SID, and revision, then executes the shared Lobby-rooted Story/Raid/Challenge scheduler without initializing WPF. `runtime-host-unavailable` remains a fail-closed diagnostic when those runtime dependencies cannot be prepared; it is no longer the expected healthy state.

The run target remains `This desktop` unless complete health reports Ready. `Local runner session` is not selectable while compatibility, loopback isolation, fresh capture, version, ACL, or runtime support is incomplete.

Settings exposes `OPEN SESSION` after provisioning compatibility and loopback isolation pass, including the expected Degraded bootstrap state before fresh Roblox capture is ready. It launches the full-screen `127.0.0.1:33991` RDP viewport with the app-owned Credential Manager entry so the owner can install Roblox, sign in, and launch it once inside the runner profile. Absent, mutating, recovery-required, compatibility-failed, and isolation-failed states cannot open the viewport. The viewport must remain visibly connected during automation; minimizing or disconnecting it invalidates capture.

## Removal

Remove and normal uninstall stop the worker and session, delete the task, firewall rules, credential, account, profile, and runner snapshot, and restore recorded system values. Cleanup then verifies every owned resource and every original registry value. Any unresolved resource is listed in status, and the helper plus journal remain so cleanup can be retried.

Owner datasets, placements, and settings are preserved unless a future installer page explicitly offers owner-data deletion. Runner data is always removed.

## Compatibility and release certification

Windows build numbers are not statically allowlisted. Compatibility is decided from the exact local binaries by the offline self-scan described above. Disposable Windows 10/11 x64 VMs remain mandatory release QA for the full mutation lifecycle: clean install, repair, Windows update, session startup, isolation, capture, rollback, remove, and uninstall. That certification tests LilacMacro's integration and cleanup; it does not populate a build catalog or permanently block a newer build whose exact binaries pass the probe.

ARM64, failed native self-scans, active remote RDP use during first setup, non-loopback exposure, stale capture, and incomplete cleanup remain rejected.

See [Installer](INSTALLER.md), [Architecture](ARCHITECTURE.md), [Privacy](../PRIVACY.md), and [Troubleshooting](TROUBLESHOOTING.md).
