using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace LilacMacro.App.Diagnostics;

internal sealed class DeepDebugArchiveFinalizer(
    string appDataRoot,
    string diagnosticsRoot,
    JsonSerializerOptions jsonOptions)
{
    public async Task<string> FinalizeAsync(
        DeepDebugSession session,
        string outcome,
        Exception? operationError,
        DateTimeOffset completedAtUtc)
    {
        await session.Evidence.CompleteAsync(session.FrameCodec, session.Limits.MaximumArchiveBytes);
        await DeepDebugFrameArtifactIndex.RewriteAsync(session.StagingDirectory, session.Evidence.Frames, jsonOptions);
        int visualProfiles = WriteVisualProfiles(session);
        await CopyLatestCrashLogAsync(session);
        await WriteReadmeAsync(session.StagingDirectory);
        await WriteManifestAsync(
            session,
            outcome,
            operationError,
            completedAtUtc,
            visualProfiles);

        string name =
            $"deep-debug-{DeepDebugSessionService.SafeName(session.Operation)}-" +
            $"{session.StartedAtUtc.ToLocalTime():yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip";
        string archive = Path.Combine(diagnosticsRoot, name);
        string temporary = Path.Combine(diagnosticsRoot, $".{name}.tmp");
        EnsureChildPath(diagnosticsRoot, archive);
        try
        {
            while (true)
            {
                CreateArchive(session.StagingDirectory, temporary);
                long length = new FileInfo(temporary).Length;
                if (length <= session.Limits.MaximumArchiveBytes) break;
                File.Delete(temporary);
                long excessBytes = length - session.Limits.MaximumArchiveBytes;
                if (session.Evidence.DropLowestPriorityEvidence(excessBytes) <= 0)
                {
                    throw new InvalidDataException(
                        $"Deep Debug archive exceeded its {session.Limits.MaximumArchiveBytes} byte hard limit after all optional frame evidence was removed.");
                }
                await DeepDebugFrameArtifactIndex.RewriteAsync(
                    session.StagingDirectory,
                    session.Evidence.Frames,
                    jsonOptions);
                await WriteManifestAsync(
                    session,
                    outcome,
                    operationError,
                    completedAtUtc,
                    visualProfiles);
            }
            File.Move(temporary, archive, overwrite: false);
            return archive;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static int WriteVisualProfiles(DeepDebugSession session)
    {
        try { return DeepDebugVisualProfileSnapshotWriter.Write(session); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            File.WriteAllText(
                Path.Combine(session.StagingDirectory, "visual-profile-copy-error.txt"),
                DeepDebugRedactor.Redact(error.ToString()));
            return 0;
        }
    }

    private async Task CopyLatestCrashLogAsync(DeepDebugSession session)
    {
        string source = Path.Combine(appDataRoot, "logs", "latest-crash.txt");
        if (!File.Exists(source)) return;
        try
        {
            await using FileStream stream = new(
                source,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                8192,
                useAsync: true);
            long retainedBytes = Math.Min(stream.Length, session.Limits.CrashLogBytes);
            stream.Seek(-retainedBytes, SeekOrigin.End);
            byte[] buffer = new byte[retainedBytes];
            await stream.ReadExactlyAsync(buffer);
            string text = Encoding.UTF8.GetString(buffer);
            await File.WriteAllTextAsync(
                Path.Combine(session.StagingDirectory, "latest-crash-sanitized.txt"),
                DeepDebugRedactor.Redact(text),
                new UTF8Encoding(false));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            await File.WriteAllTextAsync(
                Path.Combine(session.StagingDirectory, "latest-crash-copy-error.txt"),
                DeepDebugRedactor.Redact(error.Message));
        }
    }

    private async Task WriteManifestAsync(
        DeepDebugSession session,
        string outcome,
        Exception? operationError,
        DateTimeOffset completedAtUtc,
        int visualProfiles)
    {
        DeepDebugManifest manifest = new(
            3,
            session.Operation,
            outcome,
            DeepDebugSessionService.GetVersion(),
            session.StartedAtUtc,
            completedAtUtc,
            completedAtUtc - session.StartedAtUtc,
            Volatile.Read(ref session.ArtifactCount),
            Volatile.Read(ref session.EventCount),
            Volatile.Read(ref session.InputEventCount),
            session.Evidence.RetainedFrameCount,
            session.Evidence.DiscardedFrameCount +
                Volatile.Read(ref session.DiscardedArtifactCount),
            Volatile.Read(ref session.WrittenEventCount),
            Volatile.Read(ref session.DiscardedEventCount),
            session.TimelineTruncated,
            session.Evidence.WindowCount,
            session.Evidence.DiscardedWindowCount,
            session.Evidence.TransitionFrameCount,
            session.Evidence.AvifFrameCount,
            session.Evidence.LossyFrameCount,
            session.Evidence.RetainedBytes,
            session.Limits.MaximumArchiveBytes,
            visualProfiles,
            session.Evidence.IsOptimized
                ? "Events and actions cover the run subject only to explicit archive safety bounds. During capture the newest ten seconds remain PNG; older ordinary frames use decode-verified quality-20 AVIF. At finalization, important frames use pixel-exact AVIF only when smaller, otherwise PNG. Compression was insufficient, so only enough lower-priority evidence was removed to remain below the hard limit."
                : "Events and actions cover the run subject only to explicit archive safety bounds. During capture the newest ten seconds remain PNG; older ordinary frames use decode-verified quality-20 AVIF. At finalization, important frames use pixel-exact AVIF only when smaller, otherwise PNG. No frames were pruned.",
            session.WriterFailure is null
                ? null
                : DeepDebugRedactor.Redact(session.WriterFailure.ToString()),
            operationError is null
                ? null
                : DeepDebugRedactor.Redact(operationError.ToString()),
            "Private-server links, Discord webhooks, Windows usernames, and profile paths are redacted. Captured Roblox pixels can still contain personal game data.");
        await WriteJsonAsync(Path.Combine(session.StagingDirectory, "manifest.json"), manifest);
    }

    private Task WriteJsonAsync<T>(string path, T value)
    {
        string json = JsonSerializer.Serialize(value, jsonOptions);
        return File.WriteAllTextAsync(
            path,
            DeepDebugRedactor.Redact(json),
            new UTF8Encoding(false));
    }

    private static Task WriteReadmeAsync(string staging) => File.WriteAllTextAsync(
        Path.Combine(staging, "README.md"),
        "# LilacMacro Deep Debug\n\n" +
        "Start with `manifest.json`, then read `timeline.md` or `events.jsonl`. " +
        "The complete one-second frame stream is retained until it reaches the archive limit. " +
        "Older ordinary frames can be decode-verified lossy AVIF; important frames are pixel-exact AVIF or PNG. " +
        "Under archive pressure, only enough low-priority frames are removed to stay below that limit, " +
        "so a timeline link can then refer to evidence removed by the retention policy. " +
        "`visual-profiles/` contains bounded immutable revisions consulted by this run. " +
        "Coordinates are Roblox client-relative half-open rectangles.\n",
        new UTF8Encoding(false));

    private static void CreateArchive(string sourceDirectory, string archivePath)
    {
        using FileStream output = new(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using ZipArchive archive = new(output, ZipArchiveMode.Create);
        foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            CompressionLevel compression = file.EndsWith(".avif", StringComparison.OrdinalIgnoreCase)
                ? CompressionLevel.NoCompression
                : CompressionLevel.Optimal;
            ZipArchiveEntry entry = archive.CreateEntry(relative, compression);
            using Stream input = File.OpenRead(file);
            using Stream destination = entry.Open();
            input.CopyTo(destination);
        }
    }

    private static void EnsureChildPath(string root, string path)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Deep Debug output resolved outside the diagnostics folder.");
    }
}
