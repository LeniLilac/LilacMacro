namespace LilacMacro.Core.LocalSession;

public static class LocalSessionSetupVerbPolicy
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "install",
        "repair",
        "remove",
        "uninstall-cleanup",
    };

    public static bool IsAllowed(string? verb) => verb is not null && Allowed.Contains(verb);

    public static bool IsRemoval(string verb) =>
        string.Equals(verb, "remove", StringComparison.Ordinal)
        || string.Equals(verb, "uninstall-cleanup", StringComparison.Ordinal);
}
