using System.Text;
using System.Text.Json;

namespace LilacMacro.App.Diagnostics;

internal static class DeepDebugSessionWriter
{
    public static async Task WriteAsync(DeepDebugSession session, JsonSerializerOptions json)
    {
        string eventsPath = Path.Combine(session.StagingDirectory, "events.jsonl");
        string timelinePath = Path.Combine(session.StagingDirectory, "timeline.md");
        try
        {
            await using FileStream eventStream = new(eventsPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            await using StreamWriter events = new(eventStream, new UTF8Encoding(false));
            await using FileStream timelineStream = new(timelinePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            await using StreamWriter timeline = new(timelineStream, new UTF8Encoding(false));
            await timeline.WriteLineAsync("# Deep debug timeline\n");
            await foreach (DeepDebugWriteItem item in session.Channel.Reader.ReadAllAsync())
            {
                WriteArtifact(session, item);
                DeepDebugEventRecord record = new(
                    item.Sequence,
                    item.TimestampUtc,
                    item.Category,
                    item.Action,
                    item.ArtifactPath,
                    item.Data);
                await events.WriteLineAsync(Serialize(record, json));
                string artifact = item.ArtifactPath is null ? string.Empty : $" [{item.ArtifactPath}]({item.ArtifactPath})";
                await timeline.WriteLineAsync(
                    $"- `{item.TimestampUtc:O}` **{Escape(item.Category)} / {Escape(item.Action)}**{artifact}");
            }
            await events.FlushAsync(CancellationToken.None);
            await timeline.FlushAsync(CancellationToken.None);
        }
        catch (Exception error)
        {
            session.WriterFailure = error;
            session.Channel.Writer.TryComplete(error);
            throw;
        }
    }

    public static void PruneExpiredArtifacts(DeepDebugSession session, DateTimeOffset referenceUtc)
    {
        if (session.RetainsAllFrames) return;

        DateTimeOffset cutoff = referenceUtc.AddMinutes(-session.FrameRetentionMinutes);
        while (session.RetainedFrames.TryPeek(out DeepDebugRetainedFrame? retained, out _) &&
               retained.TimestampUtc < cutoff)
        {
            session.RetainedFrames.Dequeue();
            try
            {
                if (File.Exists(retained.Path)) File.Delete(retained.Path);
                Interlocked.Increment(ref session.DiscardedArtifactCount);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                session.RetainedFrames.Enqueue(retained, retained.TimestampUtc.UtcTicks);
                break;
            }
        }
    }

    private static void WriteArtifact(DeepDebugSession session, DeepDebugWriteItem item)
    {
        if (item.ArtifactBytes is null || item.ArtifactPath is null) return;
        string path = Path.GetFullPath(Path.Combine(session.StagingDirectory, item.ArtifactPath));
        string root = Path.GetFullPath(Path.Combine(session.StagingDirectory, "frames"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Deep debug artifact resolved outside the frame folder.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, item.ArtifactBytes);
        session.RetainedFrames.Enqueue(new DeepDebugRetainedFrame(path, item.TimestampUtc), item.TimestampUtc.UtcTicks);
        PruneExpiredArtifacts(session, item.TimestampUtc);
    }

    private static string Serialize(DeepDebugEventRecord record, JsonSerializerOptions json)
    {
        try
        {
            return DeepDebugRedactor.Redact(JsonSerializer.Serialize(record, json));
        }
        catch (Exception error)
        {
            return DeepDebugRedactor.Redact(JsonSerializer.Serialize(
                record with { Data = new { SerializationError = error.Message } },
                json));
        }
    }

    private static string Escape(string text) => text.Replace("*", "\\*", StringComparison.Ordinal);
}
