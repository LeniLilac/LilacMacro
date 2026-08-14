using LilacMacro.Core.Updates;
using LilacMacro.Core.LocalSession;

namespace LilacMacro.Windows.LocalSession;

public sealed class CoordinatedUpdateRelauncher(LocalSessionPaths paths)
{
    public async Task RelaunchAsync(string statePath, CancellationToken cancellationToken)
    {
        string validatedStatePath = ValidateStatePath(statePath);
        CoordinatedUpdateState state = CoordinatedUpdateText.ParseState(
            await File.ReadAllTextAsync(validatedStatePath, cancellationToken).ConfigureAwait(false));
        if (!string.Equals(Path.GetFullPath(state.RequestPath), Path.GetFullPath(paths.UpdateRequestPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The coordinated update request path is invalid.");

        LocalSessionProvisioningManifest? manifest = await new ProvisioningJournalStore(paths)
            .ReadAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<LocalRunnerProfile> profiles = LocalSessionProfileCompatibility.ResolveProfiles(manifest);
        string[] unknown = state.ActiveRunnerIds
            .Where(id => profiles.All(profile => profile.Id != id))
            .ToArray();
        if (unknown.Length > 0)
            throw new InvalidDataException("The update state names a runner that is not owned by LilacMacro.");

        DeleteIfPresent(paths.UpdateRequestPath);
        RunnerScheduledTaskManager tasks = new();
        foreach (string runnerId in state.ActiveRunnerIds.Distinct(StringComparer.Ordinal))
            tasks.Run(runnerId);
    }

    public async Task RelaunchConfiguredAsync(CancellationToken cancellationToken)
    {
        LocalSessionProvisioningManifest? manifest = await new ProvisioningJournalStore(paths)
            .ReadAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<LocalRunnerProfile> profiles = LocalSessionProfileCompatibility.ResolveProfiles(manifest);
        RunnerScheduledTaskManager tasks = new();
        List<Exception> failures = [];
        foreach (LocalRunnerProfile profile in profiles)
        {
            try { tasks.Run(profile.Id); }
            catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count > 0)
            throw new AggregateException("One or more configured runner UIs could not be relaunched.", failures);
    }

    internal static string ValidateStatePath(string statePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        string root = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LilacMacro",
            "updates")) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(statePath);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(fullPath), "update-state.txt", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The coordinated update state path is outside LilacMacro's update cache.");
        }
        return fullPath;
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
