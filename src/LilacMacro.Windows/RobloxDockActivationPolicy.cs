namespace LilacMacro.Windows;

public static class RobloxDockActivationPolicy
{
    public static bool CanMaintainDock(
        bool ownerActive,
        bool docked,
        bool sourceForeground,
        bool sourceFocusAllowed) =>
        ownerActive || docked && sourceForeground && sourceFocusAllowed;
}
