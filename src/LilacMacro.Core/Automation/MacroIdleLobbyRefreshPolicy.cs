namespace LilacMacro.Core.Automation;

public static class MacroIdleLobbyRefreshPolicy
{
    public static readonly TimeSpan MaximumTrustedLobbyIdle = TimeSpan.FromMinutes(5);

    public static bool RequiresRefresh(TimeSpan idleDuration)
    {
        if (idleDuration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(idleDuration));
        return idleDuration >= MaximumTrustedLobbyIdle;
    }
}
