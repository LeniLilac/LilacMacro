namespace LilacMacro.Core.LocalSession;

public static class LocalSessionProfileCompatibility
{
    public static IReadOnlyList<LocalRunnerProfile> ResolveProfiles(
        LocalSessionProvisioningManifest? manifest)
    {
        if (manifest is null) return [];
        if (manifest.RunnerProfiles.Count > 0) return manifest.RunnerProfiles;
        if (string.IsNullOrWhiteSpace(manifest.RunnerSid)) return [];
        return [LegacyProfile(manifest)];
    }

    public static LocalSessionProvisioningManifest? NormalizeManifest(
        LocalSessionProvisioningManifest? manifest)
    {
        if (manifest is null || manifest.RunnerProfiles.Count > 0) return manifest;
        if (string.IsNullOrWhiteSpace(manifest.RunnerSid)) return manifest;
        return manifest with { RunnerProfiles = [LegacyProfile(manifest)] };
    }

    private static LocalRunnerProfile LegacyProfile(LocalSessionProvisioningManifest manifest) => new()
    {
        Id = "runner-1",
        DisplayName = "Runner 1",
        AccountName = manifest.RunnerAccountName,
        RunnerSid = manifest.RunnerSid,
        Slot = 1,
        LoopbackAddress = "127.0.0.2",
        ConfigurationMode = RunnerConfigurationMode.Shared,
    };
}
