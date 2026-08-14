using System.Text;
using System.Text.Json;

namespace LilacMacro.App.Diagnostics;

internal static class DeepDebugVisualProfileSnapshotWriter
{
    private const int MaximumProfiles = 64;
    private const int MaximumFilesPerProfile = 32;
    private const long MaximumProfileBytes = 8 * 1024 * 1024;
    private const long MaximumTotalBytes = 32 * 1024 * 1024;

    public static int Write(DeepDebugSession session)
    {
        if (session.VisualProfiles.IsEmpty) return 0;
        string outputRoot = Path.Combine(session.StagingDirectory, "visual-profiles");
        List<object> index = [];
        long totalBytes = 0;
        int copied = 0;
        foreach (DeepDebugVisualProfileReference reference in session.VisualProfiles.Values
                     .OrderBy(item => item.ProfileId, StringComparer.Ordinal)
                     .Take(MaximumProfiles))
        {
            string revisionRoot = Path.GetFullPath(reference.RevisionDirectory);
            if (!Directory.Exists(revisionRoot) || IsReparsePoint(revisionRoot)) continue;
            string revision = SafeSegment(Path.GetFileName(revisionRoot));
            string profileId = SafeSegment(reference.ProfileId);
            FileInfo[] files = new DirectoryInfo(revisionRoot)
                .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                .Where(IsAllowedProfileFile)
                .Take(MaximumFilesPerProfile + 1)
                .ToArray();
            if (files.Length == 0 || files.Length > MaximumFilesPerProfile ||
                files.All(file => !file.Name.Equals("profile.json", StringComparison.OrdinalIgnoreCase)) ||
                files.Any(file => IsReparsePoint(file.FullName)))
            {
                continue;
            }

            long profileBytes = files.Sum(file => file.Length);
            FileInfo? locator = SafeLocator(reference.LocatorPath);
            if (locator is null)
            {
                session.WriterFailure ??= new InvalidDataException(
                    $"Visual locator was unavailable for profile {reference.ProfileId}.");
            }
            if (locator is not null) profileBytes = checked(profileBytes + locator.Length);
            if (profileBytes > MaximumProfileBytes || totalBytes + profileBytes > MaximumTotalBytes)
                continue;

            string destination = Path.Combine(outputRoot, profileId, revision);
            Directory.CreateDirectory(destination);
            foreach (FileInfo file in files)
            {
                Copy(file, Path.Combine(destination, file.Name));
            }
            if (locator is not null) Copy(locator, Path.Combine(destination, "locator.json"));
            totalBytes += profileBytes;
            copied++;
            index.Add(new
            {
                ProfileId = reference.ProfileId,
                Revision = Path.GetFileName(revisionRoot),
                Files = files.Length + (locator is null ? 0 : 1),
                Bytes = profileBytes,
            });
        }

        if (copied > 0)
        {
            string json = JsonSerializer.Serialize(index, new JsonSerializerOptions
            {
                WriteIndented = true,
            });
            File.WriteAllText(
                Path.Combine(outputRoot, "index.json"),
                DeepDebugRedactor.Redact(json),
                new UTF8Encoding(false));
        }
        return copied;
    }

    private static void Copy(FileInfo source, string destination)
    {
        if (source.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            string redacted = DeepDebugRedactor.Redact(File.ReadAllText(source.FullName));
            File.WriteAllText(destination, redacted, new UTF8Encoding(false));
            return;
        }
        File.Copy(source.FullName, destination, overwrite: false);
    }

    private static bool IsAllowedProfileFile(FileInfo file) =>
        file.Name.Equals("profile.json", StringComparison.OrdinalIgnoreCase) ||
        file.Extension.Equals(".pgm", StringComparison.OrdinalIgnoreCase);

    private static FileInfo? SafeLocator(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        FileInfo locator = new(Path.GetFullPath(path));
        return locator.Exists &&
               locator.Name.Equals("locator.json", StringComparison.OrdinalIgnoreCase) &&
               !IsReparsePoint(locator.FullName) &&
               locator.Length <= 256 * 1024
            ? locator
            : null;
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static string SafeSegment(string value)
    {
        string safe = new(value.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '-').ToArray());
        safe = safe.Trim('-', '_');
        return safe.Length == 0 ? "profile" : safe[..Math.Min(safe.Length, 128)];
    }
}
