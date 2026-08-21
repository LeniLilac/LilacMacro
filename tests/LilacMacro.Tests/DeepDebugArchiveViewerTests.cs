using System.IO.Compression;
using LilacMacro.App.DeepDebugViewer;

namespace LilacMacro.Tests;

public sealed class DeepDebugArchiveViewerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LilacMacro.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task OpenAsync_IndexesCamelCaseFramesAndClientInputMarkers()
    {
        string path = CreateArchive(
            """
            {"operation":"team swap","outcome":"success","appVersion":"1.0.13","runtime":"00:00:02","artifacts":2,"events":3,"inputEvents":1,"visualProfiles":1}
            """,
            """
            {"sequence":1,"timestampUtc":"2026-08-08T01:00:00Z","category":"frame","action":"live-client","artifact":"frames/first.png","data":{"size":{"width":1366,"height":700}}}
            {"sequence":2,"timestampUtc":"2026-08-08T01:00:00.100Z","category":"input","action":"click_started","data":{"data":{"point":{"x":444,"y":333}}}}
            {"sequence":3,"timestampUtc":"2026-08-08T01:00:00.200Z","category":"frame","action":"ocr-crop","artifact":"frames/second.png","data":{"crop":{"x":400,"y":300,"width":200,"height":100}}}
            """);

        using DeepDebugArchive archive = await DeepDebugArchive.OpenAsync(path);

        Assert.Equal("team swap", archive.Manifest.Operation);
        Assert.Equal(2, archive.Frames.Count);
        Assert.Equal(new DeepDebugSourceRegion(0, 0, 1366, 700), archive.Frames[0].SourceRegion);
        DeepDebugInputMarker marker = Assert.Single(archive.GetInputMarkers(0));
        Assert.Equal((444, 333), (marker.LocalX, marker.LocalY));
        Assert.Equal("CLICK", marker.Kind);
    }

    [Fact]
    public async Task OpenAsync_SkipsMalformedEventAndMapsCropRelativeMarker()
    {
        string path = CreateArchive(
            "{\"operation\":\"crop\",\"outcome\":\"failed\"}",
            """
            not-json
            {"sequence":10,"timestampUtc":"2026-08-08T01:00:00Z","category":"frame","action":"ocr-crop","artifact":"frames/first.png","data":{"crop":{"x":400,"y":300,"width":200,"height":100}}}
            {"sequence":11,"timestampUtc":"2026-08-08T01:00:00.100Z","category":"input","action":"scroll_started","data":{"point":{"x":450,"y":350},"wheelDelta":-600}}
            """);

        using DeepDebugArchive archive = await DeepDebugArchive.OpenAsync(path);

        Assert.Equal(1, archive.MalformedEventLines);
        DeepDebugInputMarker marker = Assert.Single(archive.GetInputMarkers(0));
        Assert.Equal((50, 50), (marker.LocalX, marker.LocalY));
        Assert.Equal(-600, marker.WheelDelta);
    }

    [Fact]
    public async Task OpenAsync_IndexesAvifFrames()
    {
        string path = CreateArchive(
            "{\"operation\":\"avif\",\"outcome\":\"success\"}",
            "{\"sequence\":1,\"timestampUtc\":\"2026-08-08T01:00:00Z\",\"category\":\"frame\",\"action\":\"live-client\",\"artifact\":\"frames/first.avif\"}");
        using (ZipArchive update = ZipFile.Open(path, ZipArchiveMode.Update))
            WriteBytes(update, "frames/first.avif", [1, 2, 3]);

        using DeepDebugArchive archive = await DeepDebugArchive.OpenAsync(path);

        Assert.Contains(archive.Frames, frame => frame.Path == "frames/first.avif");
    }

    private string CreateArchive(string manifest, string events)
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(archive, "manifest.json", manifest);
        Write(archive, "events.jsonl", events.Replace("\r", string.Empty));
        WriteBytes(archive, "frames/first.png", [1]);
        WriteBytes(archive, "frames/second.png", [2]);
        return path;
    }

    private static void Write(ZipArchive archive, string name, string value)
    {
        using StreamWriter writer = new(archive.CreateEntry(name).Open());
        writer.Write(value);
    }

    private static void WriteBytes(ZipArchive archive, string name, byte[] value)
    {
        using Stream stream = archive.CreateEntry(name).Open();
        stream.Write(value);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
