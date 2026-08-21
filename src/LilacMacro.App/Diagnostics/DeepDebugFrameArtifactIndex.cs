using System.Text;
using System.Text.Json;

namespace LilacMacro.App.Diagnostics;

internal static class DeepDebugFrameArtifactIndex
{
    public static async Task RewriteAsync(
        string stagingDirectory,
        IReadOnlyList<DeepDebugEvidenceFrame> frames,
        JsonSerializerOptions options)
    {
        Dictionary<string, string> replacements = frames
            .Where(frame => frame.OriginalArtifactPath != frame.ArtifactPath)
            .ToDictionary(frame => frame.OriginalArtifactPath, frame => frame.ArtifactPath, StringComparer.Ordinal);
        string indexPath = Path.Combine(stagingDirectory, "frames", "index.json");
        object[] index = frames.Where(frame => !frame.Deleted).Select(frame => new
        {
            originalPath = frame.OriginalArtifactPath,
            path = frame.ArtifactPath,
            format = frame.Format,
            encoding = frame.EncodingMode,
            quality = frame.Quality,
            originalBytes = frame.OriginalLength,
            retainedBytes = frame.Length,
            validation = frame.Validation,
            important = frame.IsImportant,
        }).ToArray();
        await File.WriteAllTextAsync(indexPath, JsonSerializer.Serialize(index, options), new UTF8Encoding(false));
        if (replacements.Count == 0) return;
        foreach (string name in new[] { "events.jsonl", "timeline.md" })
        {
            string path = Path.Combine(stagingDirectory, name);
            string text = await File.ReadAllTextAsync(path);
            foreach ((string before, string after) in replacements)
                text = text.Replace(before, after, StringComparison.Ordinal);
            await File.WriteAllTextAsync(path, text, new UTF8Encoding(false));
        }
    }
}
