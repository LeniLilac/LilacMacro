using LilacMacro.Core.Services;

namespace LilacMacro.App.Diagnostics;

internal sealed record DeepDebugStorageState(
    long FreeBytes,
    long ExistingArchiveBytes,
    int ConfiguredStorageGiB,
    long EffectiveStorageBytes,
    bool CapturePaused)
{
    public int EffectiveStorageGiB => (int)Math.Max(
        1,
        EffectiveStorageBytes / DiagnosticUploadPolicy.OneGiB);
}

internal static class DeepDebugStoragePolicy
{
    public const int MinimumStorageGiB = 1;
    public const int MaximumStorageGiB = 1_024;
    public const int SingleArchiveStorageGiB = 3;
    public const int MediumStorageGiB = 10;
    public const int LargeStorageGiB = 30;

    public static long MinimumFreeBytes { get; } =
        3 * DiagnosticUploadPolicy.OneGiB;

    public static int RecommendedStorageGiB(long freeBytes) => freeBytes switch
    {
        > 200L * DiagnosticUploadPolicy.OneGiB => LargeStorageGiB,
        > 50L * DiagnosticUploadPolicy.OneGiB => MediumStorageGiB,
        _ => SingleArchiveStorageGiB,
    };

    public static DeepDebugStorageState Evaluate(
        int configuredStorageGiB,
        long freeBytes,
        long existingArchiveBytes)
    {
        int normalized = Math.Clamp(
            configuredStorageGiB,
            MinimumStorageGiB,
            MaximumStorageGiB);
        long requestedBytes = checked((long)normalized * DiagnosticUploadPolicy.OneGiB);
        long availablePoolBytes = SaturatingAdd(
            Math.Max(0, freeBytes),
            Math.Max(0, existingArchiveBytes));
        return new(
            Math.Max(0, freeBytes),
            Math.Max(0, existingArchiveBytes),
            normalized,
            Math.Min(requestedBytes, availablePoolBytes),
            freeBytes < MinimumFreeBytes);
    }

    public static long ReadAvailableFreeBytes(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root)) return 0;
        try { return new DriveInfo(root).AvailableFreeSpace; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static long SaturatingAdd(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;
}
