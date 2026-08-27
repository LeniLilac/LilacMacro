namespace LilacMacro.Core.Automation;

public enum NavigationInputKind
{
    ConfiguredKey,
    FreshButtonFallback,
}

public static class NavigationInputPolicy
{
    public static NavigationInputKind Select(bool hasConfiguredKey, int completedActionAttempts)
    {
        if (completedActionAttempts < 0)
            throw new ArgumentOutOfRangeException(nameof(completedActionAttempts));
        return hasConfiguredKey && completedActionAttempts == 0
            ? NavigationInputKind.ConfiguredKey
            : NavigationInputKind.FreshButtonFallback;
    }
}
