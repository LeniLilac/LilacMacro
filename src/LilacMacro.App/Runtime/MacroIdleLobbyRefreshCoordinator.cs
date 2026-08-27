using LilacMacro.App.Diagnostics;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Runtime;

internal sealed class MacroIdleLobbyRefreshCoordinator(
    Action<string> log,
    DeepDebugSessionService deepDebug,
    Func<CancellationToken, Task> refreshLobby)
{
    private DateTimeOffset? _idleStartedAt;

    public void ObserveIdle(DateTimeOffset now) => _idleStartedAt ??= now;

    public async Task<bool> RefreshIfRequiredAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (_idleStartedAt is not DateTimeOffset startedAt) return false;
        _idleStartedAt = null;
        TimeSpan idleDuration = now - startedAt;
        if (!MacroIdleLobbyRefreshPolicy.RequiresRefresh(idleDuration)) return false;

        log($"LONG IDLE COMPLETE | REFRESHING LOBBY | {idleDuration:c}");
        deepDebug.RecordEvent("macro", "idle_lobby_refresh", new { IdleDuration = idleDuration });
        await refreshLobby(cancellationToken);
        return true;
    }
}
