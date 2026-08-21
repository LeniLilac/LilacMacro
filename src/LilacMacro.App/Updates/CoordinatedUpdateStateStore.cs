using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using LilacMacro.Core.LocalSession;
using LilacMacro.Core.Updates;
using LilacMacro.Windows.LocalSession;

namespace LilacMacro.App.Updates;

internal static class CoordinatedUpdateStateStore
{
    private static readonly string[] AppProcessNames =
    [
        "LilacMacro",
        "LilacMacro.DatasetBuilder",
        "LilacMacro.RuntimeLab",
        "LilacMacro.DeepDebugViewer",
    ];

    public static string CacheRoot(Guid operationId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LilacMacro",
        "updates",
        operationId.ToString("N"));

    public static async Task<string> WriteAsync(
        Guid operationId,
        VerifiedUpdateRelease release,
        string installerSha256,
        CancellationToken cancellationToken)
    {
        LocalSessionPaths paths = LocalSessionPaths.CreateDefault(AppContext.BaseDirectory);
        int[] participantPids = DiscoverParticipants();
        LocalSessionProvisioningManifest? manifest = await new ProvisioningJournalStore(paths)
            .ReadAsync(cancellationToken).ConfigureAwait(false);
        string[] activeRunners = LocalSessionProfileCompatibility.ResolveProfiles(manifest)
            .Where(profile => ShouldRelaunchRunner(RunnerSessionManager.Inspect(profile.AccountName)))
            .Select(profile => profile.Id)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        CoordinatedUpdateState state = new(
            operationId,
            release.Version,
            installerSha256,
            paths.UpdateRequestPath,
            participantPids,
            activeRunners);
        string root = CacheRoot(operationId);
        Directory.CreateDirectory(root);
        string statePath = Path.Combine(root, "update-state.txt");
        string temporary = statePath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                CoordinatedUpdateText.SerializeState(state),
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporary, statePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return statePath;
    }

    internal static bool ShouldRelaunchRunner(RunnerSessionObservation observation) =>
        observation.SessionId is not null
        && observation.State is RunnerSessionConnectionState.Active or RunnerSessionConnectionState.Disconnected;

    private static int[] DiscoverParticipants()
    {
        HashSet<int> pids = [Environment.ProcessId];
        foreach (string processName in AppProcessNames)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            try
            {
                foreach (Process process in processes)
                {
                    try
                    {
                        pids.Add(process.Id);
                    }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }
            }
            finally
            {
                foreach (Process process in processes) process.Dispose();
            }
        }
        return pids.Order().ToArray();
    }

}
