using LilacMacro.Core.LocalSession;
using Microsoft.Win32;

namespace LilacMacro.Windows.LocalSession;

internal sealed class LocalRunnerProfileProvisioner(LocalSessionPaths paths)
{
    private readonly RunnerAccountManager accounts = new();
    private readonly RunnerCredentialManager credentials = new();
    private readonly RunnerScheduledTaskManager tasks = new();

    public static LocalRunnerProfile Create(int slot, RunnerConfigurationMode mode)
    {
        if (slot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(slot));
        return new LocalRunnerProfile
        {
            Id = $"runner-{slot}",
            DisplayName = $"Runner {slot}",
            AccountName = slot == 1 ? "LilacMacroRunner" : $"LilacMacroRunner{slot}",
            Slot = slot,
            LoopbackAddress = $"127.0.0.{slot + 1}",
            ConfigurationMode = mode,
        };
    }

    public async Task<LocalRunnerProfile> ProvisionAsync(
        LocalRunnerProfile profile,
        string ownerSid,
        IReadOnlyList<LocalRunnerProfile> allProfiles,
        bool repair,
        CancellationToken cancellationToken)
    {
        string password = repair && accounts.Exists(profile.AccountName)
            ? ReadOrRotateCredential(profile)
            : RunnerCredentialManager.CreateRandomPassword();
        string runnerSid = accounts.EnsureCreated(profile.AccountName, password);
        accounts.SetPassword(profile.AccountName, password);
        LocalRunnerProfile provisioned = profile with { RunnerSid = runnerSid };
        LocalRunnerProfile[] securedProfiles = allProfiles
            .Select(item => item.Id == provisioned.Id ? provisioned : item)
            .Where(item => !string.IsNullOrWhiteSpace(item.RunnerSid))
            .ToArray();
        LocalSessionAclManager.SecureInstanceRoots(paths, ownerSid, securedProfiles);

        string qualifiedRunner = RunnerScheduledTaskManager.QualifyLocalAccount(profile.AccountName, Environment.MachineName);
        credentials.WriteSecret(paths.SecretCredentialTargetFor(profile), qualifiedRunner, password);
        credentials.DeleteRdp(paths.CredentialTargetFor(profile));
        credentials.WriteRdp(paths.CredentialTargetFor(profile), qualifiedRunner, password);
        if (profile.Slot == 1)
        {
            credentials.DeleteSecret(paths.SecretCredentialTarget);
            credentials.Delete(paths.PortCredentialTarget);
            credentials.Delete(paths.LegacyCredentialTarget);
        }

        RunnerProfileStore store = new(paths, profile.Id);
        DeleteIfPresent(paths.ProfileReceiptPathFor(profile.Id));
        DeleteIfPresent(paths.ProfileFailurePathFor(profile.Id));
        await store.WritePolicyAsync(new RunnerProfilePolicy(), cancellationToken).ConfigureAwait(false);
        int profileExit = await new RunnerProcessLauncher().RunAndWaitAsync(
            profile.AccountName,
            password,
            paths.WorkerPath,
            ["--apply-profile-policy", profile.Id, paths.ProfilePolicyPathFor(profile.Id), paths.ProfileReceiptPathFor(profile.Id)],
            TimeSpan.FromMinutes(5),
            cancellationToken).ConfigureAwait(false);
        if (profileExit != 0)
        {
            RunnerProfileFailure? failure = await store.ReadFailureAsync(cancellationToken).ConfigureAwait(false);
            string detail = failure is null
                ? $"The controlled {profile.DisplayName} policy pass failed without a diagnostic receipt."
                : $"The controlled {profile.DisplayName} policy pass failed ({failure.FailureCode}: {failure.Detail}).";
            throw new InvalidOperationException(detail);
        }
        RunnerProfileReceipt? receipt = await store.ReadReceiptAsync(cancellationToken).ConfigureAwait(false);
        if (receipt is null || receipt.RunnerSid != runnerSid || receipt.PolicyVersion != RunnerProfilePolicy.CurrentVersion)
            throw new InvalidDataException($"The controlled {profile.DisplayName} profile could not be verified.");

        tasks.Register(provisioned, paths.AppPath, paths.ConfigurationRootFor(provisioned));
        return provisioned;
    }

    public void Remove(LocalRunnerProfile profile, bool removeConfiguration)
    {
        RunnerSessionManager.LogoffAll(profile.AccountName);
        tasks.Remove(profile.Id);
        credentials.DeleteRdp(paths.CredentialTargetFor(profile));
        credentials.DeleteSecret(paths.SecretCredentialTargetFor(profile));
        string runnerSid = string.IsNullOrWhiteSpace(profile.RunnerSid)
            ? accounts.TryResolveSid(profile.AccountName) ?? string.Empty
            : profile.RunnerSid;
        string? profilePath = GetWindowsProfilePath(runnerSid);
        accounts.Remove(profile.AccountName, profilePath);
        DeleteOwnedDirectory(paths.ProfileRoot(profile.Id), paths.ProfilesRoot);
        if (removeConfiguration && profile.ConfigurationMode == RunnerConfigurationMode.Isolated)
            DeleteOwnedDirectory(paths.ConfigurationRootFor(profile), paths.ConfigurationsRoot);
    }

    public void RegisterUi(LocalRunnerProfile profile) =>
        tasks.Register(profile, paths.AppPath, paths.ConfigurationRootFor(profile));

    public static string? GetWindowsProfilePath(string sid)
    {
        if (string.IsNullOrWhiteSpace(sid)) return null;
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}");
        return key?.GetValue("ProfileImagePath") is string value ? Environment.ExpandEnvironmentVariables(value) : null;
    }

    private string ReadOrRotateCredential(LocalRunnerProfile profile)
    {
        List<string> targets = [paths.SecretCredentialTargetFor(profile)];
        if (profile.Slot == 1) targets.Add(paths.SecretCredentialTarget);
        foreach (string target in targets)
        {
            try { return credentials.ReadPassword(target); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
        }
        return RunnerCredentialManager.CreateRandomPassword();
    }

    private static void DeleteOwnedDirectory(string path, string allowedParent)
    {
        string root = Path.GetFullPath(path);
        string parent = Path.GetFullPath(allowedParent) + Path.DirectorySeparatorChar;
        if (!root.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Runner path escaped the owned ProgramData root.");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
