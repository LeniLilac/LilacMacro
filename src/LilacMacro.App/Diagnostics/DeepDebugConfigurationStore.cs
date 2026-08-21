namespace LilacMacro.App.Diagnostics;

internal sealed class DeepDebugConfigurationStore
{
    private readonly string _diagnosticsRoot;
    private readonly DeepDebugOptionsStore _optionsStore;
    private readonly DeepDebugRetentionStore _retentionStore;
    private readonly Func<string, long> _availableFreeBytes;

    public DeepDebugConfigurationStore(
        string appDataRoot,
        string diagnosticsRoot,
        Func<string, long>? availableFreeBytes = null)
    {
        _diagnosticsRoot = diagnosticsRoot;
        _optionsStore = new DeepDebugOptionsStore(appDataRoot);
        _retentionStore = new DeepDebugRetentionStore(diagnosticsRoot);
        _availableFreeBytes = availableFreeBytes ?? DeepDebugStoragePolicy.ReadAvailableFreeBytes;
    }

    public DeepDebugOptions Load()
    {
        DeepDebugOptions local = _optionsStore.Load();
        long freeBytes = _availableFreeBytes(_diagnosticsRoot);
        int configuredStorage = _retentionStore.Load(
            DeepDebugStoragePolicy.RecommendedStorageGiB(freeBytes));
        int affordableStorage = DeepDebugStoragePolicy.Evaluate(
            configuredStorage,
            freeBytes,
            ExistingArchiveBytes()).EffectiveStorageGiB;
        return local with
        {
            MaximumArchiveStorageGiB = Math.Min(configuredStorage, affordableStorage),
        };
    }

    public DeepDebugStorageState StorageState(DeepDebugOptions? options = null)
    {
        DeepDebugOptions current = options ?? Load();
        return DeepDebugStoragePolicy.Evaluate(
            current.MaximumArchiveStorageGiB,
            _availableFreeBytes(_diagnosticsRoot),
            ExistingArchiveBytes());
    }

    public async Task<DeepDebugOptions> UpdateAsync(
        bool? enabled,
        int? maximumArchiveStorageGiB)
    {
        DeepDebugOptions current = Load();
        int requested = DeepDebugOptions.NormalizeMaximumArchiveStorageGiB(
            maximumArchiveStorageGiB ?? current.MaximumArchiveStorageGiB);
        DeepDebugStorageState state = DeepDebugStoragePolicy.Evaluate(
            requested,
            _availableFreeBytes(_diagnosticsRoot),
            ExistingArchiveBytes());
        int affordable = Math.Min(requested, state.EffectiveStorageGiB);
        int savedStorage = maximumArchiveStorageGiB is null
            ? current.MaximumArchiveStorageGiB
            : await _retentionStore.SaveAsync(affordable);
        DeepDebugOptions updated = new()
        {
            Enabled = enabled ?? current.Enabled,
            MaximumArchiveStorageGiB = savedStorage,
        };
        await _optionsStore.SaveAsync(updated);
        return updated;
    }

    public void PruneArchives(DeepDebugOptions? options = null)
    {
        try
        {
            DeepDebugStorageState state = StorageState(options);
            PruneArchivesWithinBudget(_diagnosticsRoot, state.EffectiveStorageBytes);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Archive creation succeeded; shared retention cleanup can retry next session.
        }
    }

    internal static void PruneArchivesWithinBudget(string diagnosticsRoot, long maximumBytes)
    {
        DirectoryInfo root = new(diagnosticsRoot);
        if (!root.Exists) return;
        FileInfo[] archives = root
            .EnumerateFiles("deep-debug-*.zip", SearchOption.TopDirectoryOnly)
            .Where(file => (file.Attributes & FileAttributes.ReparsePoint) == 0)
            .OrderByDescending(file => file.CreationTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .ToArray();
        long retained = archives.Sum(file => file.Length);
        foreach (FileInfo archive in archives.Reverse().Take(Math.Max(0, archives.Length - 1)))
        {
            if (retained <= maximumBytes) break;
            long length = archive.Length;
            archive.Delete();
            retained -= length;
        }
    }

    private long ExistingArchiveBytes()
    {
        try { return EnumerateArchives().Sum(file => file.Length); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return 0; }
    }

    private IEnumerable<FileInfo> EnumerateArchives()
    {
        DirectoryInfo root = new(_diagnosticsRoot);
        return root.Exists
            ? root.EnumerateFiles("deep-debug-*.zip", SearchOption.TopDirectoryOnly)
                .Where(file => (file.Attributes & FileAttributes.ReparsePoint) == 0)
            : [];
    }
}
