namespace LilacMacro.Windows;

public enum RobloxDockMaintenanceAction
{
    Acquire,
    Maintain,
    Repair,
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
}
