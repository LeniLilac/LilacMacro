namespace LilacMacro.App.Runtime;

internal enum MacroLayoutProfile
{
    Full1920x1080,
    Compact1366x768,
}

internal enum MacroMinimizeBehavior
{
    KeepVisible,
    WhileRunning,
    OnApplicationStart,
}

internal static class MacroDisplayPolicy
{
    public static MacroLayoutProfile ManagedViewportLayout(int width, int height) =>
        width <= 1366 && height <= 768
            ? MacroLayoutProfile.Compact1366x768
            : MacroLayoutProfile.Full1920x1080;

    public static MacroMinimizeBehavior EffectiveMinimizeBehavior(
        MacroLayoutProfile layout,
        MacroMinimizeBehavior configured) =>
        layout == MacroLayoutProfile.Compact1366x768
            ? MacroMinimizeBehavior.WhileRunning
            : configured;

    public static MacroMinimizeBehavior ConfiguredMinimizeBehaviorForSelection(
        MacroLayoutProfile currentLayout,
        MacroLayoutProfile selectedLayout,
        MacroMinimizeBehavior selectedBehavior,
        MacroMinimizeBehavior configuredBehavior) =>
        currentLayout == MacroLayoutProfile.Full1920x1080
            && selectedLayout == MacroLayoutProfile.Full1920x1080
            ? selectedBehavior
            : configuredBehavior;

    public static bool AllowsDock(MacroLayoutProfile layout) =>
        layout == MacroLayoutProfile.Full1920x1080;

    public static (double Width, double Height) TargetSize(MacroLayoutProfile layout) => layout switch
    {
        MacroLayoutProfile.Compact1366x768 => (1366, 768),
        _ => (1920, 1080),
    };

    public static (double Width, double Height) MinimumSize(MacroLayoutProfile layout) => layout switch
    {
        MacroLayoutProfile.Compact1366x768 => (1060, 680),
        _ => (1788, 898),
    };
}
