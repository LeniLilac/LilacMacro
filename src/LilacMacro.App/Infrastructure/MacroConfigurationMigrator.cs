using LilacMacro.App.Runtime;
using LilacMacro.Windows;

namespace LilacMacro.App.Infrastructure;

internal static class MacroConfigurationMigrator
{
    public static async Task EnsureOwnerSharedConfigurationAsync(CancellationToken cancellationToken = default)
    {
        MacroInstanceContext context = MacroInstanceContext.Current;
        if (context.IsManagedRunner) return;
        string localRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LilacMacro");
        string sharedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "LilacMacro",
            "Configurations",
            "shared");
        if (!Directory.Exists(sharedRoot)) return;
        MacroSettingsStore destination = new(sharedRoot);
        if (destination.Exists) return;
        MacroSettingsStore source = new(localRoot);
        if (!source.Exists) return;
        MacroSettings settings = await source.LoadAsync(cancellationToken).ConfigureAwait(false);
        DpapiSecretProtector currentUser = new();
        DpapiSecretProtector machine = new(machineScope: true);
        settings = settings with
        {
            EncryptedPrivateServerLink = Reprotect(settings.EncryptedPrivateServerLink, currentUser, machine),
            EncryptedDiscordWebhook = Reprotect(settings.EncryptedDiscordWebhook, currentUser, machine),
        };
        await destination.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        CopyPlacementFiles(Path.Combine(localRoot, "placements"), Path.Combine(sharedRoot, "placements"));
    }

    private static string Reprotect(string value, DpapiSecretProtector source, DpapiSecretProtector destination)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return destination.Protect(source.Unprotect(value));
    }

    private static void CopyPlacementFiles(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*.json", SearchOption.TopDirectoryOnly))
        {
            string target = Path.Combine(destination, Path.GetFileName(file));
            if (!File.Exists(target)) File.Copy(file, target);
        }
    }
}
