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
        session.Evidence.Complete(completedAtUtc, session.Limits.FrameEvidenceBytes);
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
                ZipFile.CreateFromDirectory(
                    session.StagingDirectory,
                    temporary,
                    CompressionLevel.Optimal,
                    includeBaseDirectory: false);
                long length = new FileInfo(temporary).Length;
                if (length <= session.Limits.MaximumArchiveBytes) break;
                File.Delete(temporary);
                if (!session.Evidence.DropLowestPriorityEvidence())
                {
                    throw new InvalidDataException(
                        $"Deep Debug archive exceeded its {session.Limits.MaximumArchiveBytes} byte hard limit after all optional frame evidence was removed.");
                }
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
            2,
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
            session.Evidence.RetainedBytes,
            session.Limits.MaximumArchiveBytes,
            visualProfiles,
            "Events and actions cover the run subject only to explicit archive safety bounds. PNG evidence uses one-second captures around classified failures (10 seconds before and after), merged overlapping windows, and one frame at important state transitions. Lowest-priority repeated or visually similar windows are removed first when space is constrained.",
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
        "Frames are intentionally limited to classified failure windows and important state transitions; " +
        "a timeline link can therefore refer to evidence removed by the retention policy. " +
        "`visual-profiles/` contains bounded immutable revisions consulted by this run. " +
        "Coordinates are Roblox client-relative half-open rectangles.\n",
        new UTF8Encoding(false));

    private static void EnsureChildPath(string root, string path)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Deep Debug output resolved outside the diagnostics folder.");
    }
}
