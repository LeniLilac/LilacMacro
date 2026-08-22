namespace LilacMacro.App.Runtime;

internal enum MacroHotkeyTarget
{
    None,
    Macro,
    SetupTest,
}

internal static class MacroHotkeyRoutingPolicy
{
    public static MacroHotkeyTarget Resolve(
        bool setupTestRunning,
        bool macroRunning,
        bool macroPageActive)
    {
        if (setupTestRunning) return MacroHotkeyTarget.SetupTest;
        if (macroRunning || macroPageActive) return MacroHotkeyTarget.Macro;
        return MacroHotkeyTarget.None;
    }
}
