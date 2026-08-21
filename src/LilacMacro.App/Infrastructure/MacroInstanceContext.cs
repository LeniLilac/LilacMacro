using System.Security.Principal;
using System.Text.Json;

namespace LilacMacro.App.Infrastructure;

internal sealed record MacroInstanceContext(
    string Id,
    string DisplayName,
    string ConfigurationRoot,
    bool UsesMachineProtectedSecrets,
    bool IsManagedRunner)
{
    private static MacroInstanceContext current = CreateOwnerContext();

    public static MacroInstanceContext Current => current;

    public static void Initialize(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        int managedIndex = IndexOf(arguments, "--managed-instance");
        if (managedIndex < 0)
        {
            current = CreateOwnerContext();
            Environment.SetEnvironmentVariable("LILACMACRO_CONFIGURATION_ROOT", current.ConfigurationRoot);
            return;
        }
        string id = ValueAfter(arguments, managedIndex, "managed instance");
        string displayName = ValueFor(arguments, "--instance-name");
        string configurationRoot = Path.GetFullPath(ValueFor(arguments, "--configuration-root"));
        string allowedRoot = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "LilacMacro",
            "Configurations")) + Path.DirectorySeparatorChar;
        if (!configurationRoot.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The managed configuration root is outside LilacMacro ProgramData.");
        current = new MacroInstanceContext(id, displayName, configurationRoot, true, true);
        Environment.SetEnvironmentVariable("LILACMACRO_CONFIGURATION_ROOT", current.ConfigurationRoot);
    }

    private static MacroInstanceContext CreateOwnerContext()
    {
        string programData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "LilacMacro");
        string local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LilacMacro");
        string shared = Path.Combine(
            programData,
            "Configurations",
            "shared");
        string journal = Path.Combine(programData, "Session", "provisioning.json");
        string? currentSid = WindowsIdentity.GetCurrent().User?.Value;
        bool useShared = ShouldUseSharedConfiguration(
            Directory.Exists(shared),
            TryReadProvisionedOwnerSid(journal),
            currentSid);
        return new MacroInstanceContext(
            "desktop",
            "This desktop",
            useShared ? shared : local,
            useShared,
            false);
    }

    internal static bool ShouldUseSharedConfiguration(
        bool sharedDirectoryExists,
        string? provisionedOwnerSid,
        string? currentSid) =>
        sharedDirectoryExists
        && !string.IsNullOrWhiteSpace(provisionedOwnerSid)
        && !string.IsNullOrWhiteSpace(currentSid)
        && string.Equals(provisionedOwnerSid, currentSid, StringComparison.OrdinalIgnoreCase);

    private static string? TryReadProvisionedOwnerSid(string journalPath)
    {
        try
        {
            using FileStream stream = new(
                journalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using JsonDocument document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("owner_sid", out JsonElement ownerSid)
                ? ownerSid.GetString()
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static int IndexOf(IReadOnlyList<string> arguments, string option)
    {
        for (int index = 0; index < arguments.Count; index++)
            if (string.Equals(arguments[index], option, StringComparison.Ordinal)) return index;
        return -1;
    }

    private static string ValueFor(IReadOnlyList<string> arguments, string option)
    {
        int index = IndexOf(arguments, option);
        return index < 0 ? throw new InvalidDataException($"Managed launch option {option} is missing.")
            : ValueAfter(arguments, index, option);
    }

    private static string ValueAfter(IReadOnlyList<string> arguments, int index, string description)
    {
        if (index + 1 >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index + 1]))
            throw new InvalidDataException($"Managed launch {description} is missing.");
        return arguments[index + 1];
    }
}
