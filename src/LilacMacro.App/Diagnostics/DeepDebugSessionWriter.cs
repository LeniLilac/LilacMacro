using System.Text;
using System.Text.Json;

namespace LilacMacro.App.Diagnostics;

internal static class DeepDebugSessionWriter
{
    private const int TruncationMarkerReserveBytes = 2_048;

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
            long eventBytes = 0;
            long timelineBytes = await WriteBoundedAsync(
                timeline,
                "# Deep debug timeline\n",
                0,
                session.Limits.TimelineBytes);

            await foreach (DeepDebugWriteItem item in session.Channel.Reader.ReadAllAsync())
            {
                session.Evidence.ObserveEvent(
                    item.Category,
                    item.Action,
                    item.Data,
                    item.TimestampUtc);
                await WriteArtifactAsync(session, item);

                DeepDebugEventRecord record = new(
                    item.Sequence,
                    item.TimestampUtc,
                    item.Category,
                    item.Action,
                    item.ArtifactPath,
                    item.Data);
                string eventLine = Serialize(record, json) + Environment.NewLine;
                if (CanWrite(eventBytes, eventLine, session.Limits.EventBytes))
                {
                    await events.WriteAsync(eventLine);
                    eventBytes += Encoding.UTF8.GetByteCount(eventLine);
                    Interlocked.Increment(ref session.WrittenEventCount);
                }
                else
                {
                    Interlocked.Increment(ref session.DiscardedEventCount);
                }

                string artifact = item.ArtifactPath is null
                    ? string.Empty
                    : $" [{item.ArtifactPath}]({item.ArtifactPath})";
                string timelineLine =
                    $"- `{item.TimestampUtc:O}` **{Escape(item.Category)} / {Escape(item.Action)}**{artifact}{Environment.NewLine}";
                if (CanWrite(timelineBytes, timelineLine, session.Limits.TimelineBytes))
                {
                    await timeline.WriteAsync(timelineLine);
                    timelineBytes += Encoding.UTF8.GetByteCount(timelineLine);
                }
                else
                {
                    session.TimelineTruncated = true;
                }
            }

            if (Volatile.Read(ref session.DiscardedEventCount) > 0)
            {
                DeepDebugEventRecord marker = new(
                    Interlocked.Increment(ref session.Sequence),
                    DateTimeOffset.UtcNow,
                    "diagnostic",
                    "event_log_truncated",
                    null,
                    new { Discarded = Volatile.Read(ref session.DiscardedEventCount) });
                await events.WriteLineAsync(Serialize(marker, json));
            }
            if (session.TimelineTruncated)
                await timeline.WriteLineAsync("\n- **Timeline truncated at its archive safety bound.**");
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

    private static async Task WriteArtifactAsync(DeepDebugSession session, DeepDebugWriteItem item)
    {
        if (item.ArtifactBytes is null || item.ArtifactPath is null) return;
        if (item.ArtifactBytes.LongLength > session.Limits.FrameEvidenceBytes)
        {
            Interlocked.Increment(ref session.DiscardedArtifactCount);
            return;
        }
        string path = Path.GetFullPath(Path.Combine(session.StagingDirectory, item.ArtifactPath));
        string root = Path.GetFullPath(Path.Combine(session.StagingDirectory, "frames"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Deep debug artifact resolved outside the frame folder.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, item.ArtifactBytes);
        session.Evidence.RecordFrame(
            path,
            item.TimestampUtc,
            item.ArtifactBytes,
            string.Equals(item.Action, "live-client", StringComparison.Ordinal));
        await session.Evidence.OptimizeAfterFrameAsync(
            session.FrameCodec,
            item.TimestampUtc,
            session.Limits.MaximumArchiveBytes);
    }

    private static bool CanWrite(long writtenBytes, string value, long limit) =>
        writtenBytes + Encoding.UTF8.GetByteCount(value) <= limit - TruncationMarkerReserveBytes;

    private static async Task<long> WriteBoundedAsync(
        StreamWriter writer,
        string value,
        long writtenBytes,
        long limit)
    {
        if (!CanWrite(writtenBytes, value, limit)) return writtenBytes;
        await writer.WriteAsync(value);
        return writtenBytes + Encoding.UTF8.GetByteCount(value);
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
