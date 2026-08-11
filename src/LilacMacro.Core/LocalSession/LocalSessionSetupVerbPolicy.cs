namespace LilacMacro.Core.LocalSession;

public static class LocalSessionSetupVerbPolicy
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "install",
        "repair",
        "remove",
        "uninstall-cleanup",
        "add-shared",
        "add-isolated",
        "remove-profile",
    };

    public static bool IsAllowed(string? verb) => verb is not null && Allowed.Contains(verb);

    public static bool IsRemoval(string verb) =>
        string.Equals(verb, "remove", StringComparison.Ordinal)
        || string.Equals(verb, "uninstall-cleanup", StringComparison.Ordinal);

    public static bool AreArgumentsAllowed(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 1) return IsAllowed(arguments[0]) && arguments[0] != "remove-profile";
        return arguments.Count == 2
            && arguments[0] == "remove-profile"
            && arguments[1].Length is > 0 and <= 32
            && arguments[1].All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    }
}
