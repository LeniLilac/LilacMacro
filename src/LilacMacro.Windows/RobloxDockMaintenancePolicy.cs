namespace LilacMacro.Windows;

public enum RobloxDockMaintenanceAction
{
    Acquire,
    Maintain,
    Repair,
}

public enum RobloxDockInactiveAction
{
    KeepSourceVisible,
    MinimizeSource,
}

public static class RobloxDockMaintenancePolicy
{
    public static bool CanAcquire(bool awaitingOwnerExposure, bool ownerActive) =>
        !awaitingOwnerExposure || ownerActive;

    public static RobloxDockMaintenanceAction Resolve(bool hasTrackedSource, bool docked) =>
        !hasTrackedSource
            ? RobloxDockMaintenanceAction.Acquire
            : docked
                ? RobloxDockMaintenanceAction.Maintain
                : RobloxDockMaintenanceAction.Repair;

    public static RobloxDockInactiveAction ResolveInactive(bool sourceIsForeground) =>
        sourceIsForeground
            ? RobloxDockInactiveAction.KeepSourceVisible
            : RobloxDockInactiveAction.MinimizeSource;
}
