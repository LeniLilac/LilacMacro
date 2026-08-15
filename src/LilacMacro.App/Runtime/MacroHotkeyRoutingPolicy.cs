namespace LilacMacro.App.Runtime;

internal enum MacroHotkeyTarget
{
    Macro,
    SetupTest,
}

internal static class MacroHotkeyRoutingPolicy
{
    public static MacroHotkeyTarget Resolve(bool setupTestRunning) =>
        setupTestRunning ? MacroHotkeyTarget.SetupTest : MacroHotkeyTarget.Macro;
}
