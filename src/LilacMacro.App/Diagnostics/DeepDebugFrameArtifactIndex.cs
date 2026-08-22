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
            await RewriteFileAsync(path, replacements);
        }
    }

    private static async Task RewriteFileAsync(
        string path,
        IReadOnlyDictionary<string, string> replacements)
    {
        string temporary = path + ".rewrite";
        try
        {
            {
                await using FileStream input = new(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    65_536,
                    useAsync: true);
                using StreamReader reader = new(
                    input,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    65_536);
                await using FileStream output = new(
                    temporary,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    65_536,
                    useAsync: true);
                await using StreamWriter writer = new(output, new UTF8Encoding(false), 65_536);
                while (await reader.ReadLineAsync() is { } line)
                    await writer.WriteLineAsync(RewriteLine(line, replacements));
                await writer.FlushAsync();
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static string RewriteLine(
        string line,
        IReadOnlyDictionary<string, string> replacements)
    {
        const string prefix = "frames/";
        const string extension = ".png";
        int searchFrom = 0;
        int copyFrom = 0;
        StringBuilder? rewritten = null;
        while (line.IndexOf(prefix, searchFrom, StringComparison.Ordinal) is int start && start >= 0)
        {
            int extensionStart = line.IndexOf(extension, start + prefix.Length, StringComparison.Ordinal);
            if (extensionStart < 0) break;
            int end = extensionStart + extension.Length;
            string candidate = line[start..end];
            if (!replacements.TryGetValue(candidate, out string? replacement))
            {
                searchFrom = end;
                continue;
            }
            rewritten ??= new StringBuilder(line.Length);
            rewritten.Append(line, copyFrom, start - copyFrom);
            rewritten.Append(replacement);
            searchFrom = end;
            copyFrom = end;
        }
        if (rewritten is null) return line;
        rewritten.Append(line, copyFrom, line.Length - copyFrom);
        return rewritten.ToString();
    }
}
