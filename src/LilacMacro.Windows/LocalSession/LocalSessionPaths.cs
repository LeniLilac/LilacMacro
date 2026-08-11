namespace LilacMacro.Windows.LocalSession;

using LilacMacro.Core.LocalSession;

public sealed record LocalSessionPaths(
    string ProgramDataRoot,
    string InstallRoot,
    string NativePayloadRoot)
{
    public static LocalSessionPaths CreateDefault(string installRoot) => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LilacMacro"),
        Path.GetFullPath(installRoot),
        Path.Combine(Path.GetFullPath(installRoot), "native", "termwrap", "v0.6"));

    public string SessionRoot => Path.Combine(ProgramDataRoot, "Session");
    public string RunnerRoot => Path.Combine(ProgramDataRoot, "Runner");
    public string ProfilesRoot => Path.Combine(ProgramDataRoot, "Profiles");
    public string ConfigurationsRoot => Path.Combine(ProgramDataRoot, "Configurations");
    public string SharedConfigurationRoot => Path.Combine(ConfigurationsRoot, "shared");
    public string JournalPath => Path.Combine(SessionRoot, "provisioning.json");
    public string StatusPath => Path.Combine(RunnerRoot, "status.json");
    public string SnapshotPath => Path.Combine(RunnerRoot, "runtime-snapshot.json");
    public string RuntimeRoot => Path.Combine(RunnerRoot, "Runtime");
    public string OcrRoot => Path.Combine(RunnerRoot, "Ocr");
    public string ProfilePolicyPath => Path.Combine(RunnerRoot, "runner-profile-policy.json");
    public string ProfileReceiptPath => Path.Combine(RunnerRoot, "runner-profile-receipt.json");
    public string ProfileFailurePath => Path.Combine(RunnerRoot, "runner-profile-failure.json");
    public string CompatibilityCachePath => Path.Combine(SessionRoot, "compatibility-cache.json");
    public string UpdateRequestPath => Path.Combine(SessionRoot, "update-request.txt");
    public string CredentialTarget => $"TERMSRV/{FirewallIsolationManager.AuthorizedLoopbackAddress}";
    public string SecretCredentialTarget => "LilacMacro/LocalSessionRunnerSecret";
    public string PortCredentialTarget => $"TERMSRV/{FirewallIsolationManager.AuthorizedLoopbackAddress}:{TermServiceConfigurationManager.LocalPort}";
    public string LegacyCredentialTarget => $"TERMSRV/127.0.0.1:{TermServiceConfigurationManager.LocalPort}";
    public string PayloadManifestPath => Path.Combine(NativePayloadRoot, "payload.json");
    public string TermWrapPath => Path.Combine(NativePayloadRoot, "x64", "TermWrap.dll");
    public string WorkerPath => Path.Combine(InstallRoot, "LilacMacro.SessionWorker.exe");
    public string AppPath => Path.Combine(InstallRoot, "LilacMacro.exe");

    public string ProfileRoot(string profileId) => Path.Combine(ProfilesRoot, ValidateProfileId(profileId));
    public string ProfilePolicyPathFor(string profileId) => Path.Combine(ProfileRoot(profileId), "runner-profile-policy.json");
    public string ProfileReceiptPathFor(string profileId) => Path.Combine(ProfileRoot(profileId), "runner-profile-receipt.json");
    public string ProfileFailurePathFor(string profileId) => Path.Combine(ProfileRoot(profileId), "runner-profile-failure.json");
    public string ConfigurationRootFor(LocalRunnerProfile profile) => profile.ConfigurationMode == RunnerConfigurationMode.Shared
        ? SharedConfigurationRoot
        : Path.Combine(ConfigurationsRoot, ValidateProfileId(profile.Id));
    public string CredentialTargetFor(LocalRunnerProfile profile) => $"TERMSRV/{profile.LoopbackAddress}";
    public string SecretCredentialTargetFor(LocalRunnerProfile profile) => $"LilacMacro/LocalSessionRunnerSecret/{ValidateProfileId(profile.Id)}";

    private static string ValidateProfileId(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        if (profileId.Length > 32 || !profileId.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
            throw new ArgumentException("Runner profile identifier is invalid.", nameof(profileId));
        return profileId;
    }
}
