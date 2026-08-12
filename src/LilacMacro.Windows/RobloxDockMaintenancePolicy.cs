namespace LilacMacro.Windows;

public enum RobloxDockMaintenanceAction
{
    Acquire,
    Maintain,
    Repair,
}

public static class RobloxDockMaintenancePolicy
{
    public static RobloxDockMaintenanceAction Resolve(bool hasTrackedSource, bool docked) =>
        !hasTrackedSource
            ? RobloxDockMaintenanceAction.Acquire
            : docked
                ? RobloxDockMaintenanceAction.Maintain
                : RobloxDockMaintenanceAction.Repair;
}
