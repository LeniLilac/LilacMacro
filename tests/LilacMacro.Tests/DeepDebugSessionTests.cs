using System.IO.Compression;
using System.Text.Json;
using LilacMacro.App.Diagnostics;
using LilacMacro.Core.Imaging;

namespace LilacMacro.Tests;

public sealed class DeepDebugSessionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "LilacMacro.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SessionWritesAgentReadableRedactedArchive()
    {
        DeepDebugSessionService service = new(_root);
        await service.UpdateOptionsAsync(enabled: true, frameRetentionMinutes: 15);
        DeepDebugScope? scope = await service.OpenSessionAsync(
            "wire test",
            new DeepDebugOperationContext(
                "dataset-builder",
                new { Secret = "https://discord.com" + "/api/webhooks/" + "123/abc" }));

        Assert.NotNull(scope);
        service.RecordEvent("ocr", "state_evaluated", new
        {
            Path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Documents",
                "dataset"),
            Text = "Lobby",
        });
        RgbImage image = new(2, 1, [255, 0, 0, 0, 255, 0], takeOwnership: true);
        service.RecordPng(PngEncoder.Encode(image), "live-client", new { Width = 2, Height = 1 });
        await scope!.CompleteAsync("success");

        string archivePath = Assert.IsType<string>(service.LastArchivePath);
        Assert.True(File.Exists(archivePath));
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        string[] entries = archive.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Contains("manifest.json", entries);
        Assert.Contains("events.jsonl", entries);
        Assert.Contains("timeline.md", entries);
        Assert.Contains("README.md", entries);
        Assert.Contains(entries, entry => entry.StartsWith("frames/frame-", StringComparison.Ordinal));

        string events = await ReadAsync(archive, "events.jsonl");
        Assert.DoesNotContain(Environment.UserName, events, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("discord.com/api/webhooks", events, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[REDACTED WINDOWS USER]", events, StringComparison.Ordinal);
        foreach (string line in events.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using JsonDocument _ = JsonDocument.Parse(line);
        }
    }

    [Fact]
    public async Task DisabledRecorderDoesNotCreateSession()
    {
        DeepDebugSessionService service = new(_root);

        DeepDebugScope? scope = await service.OpenSessionAsync(
            "disabled",
            new DeepDebugOperationContext("test"));

        Assert.Null(scope);
        Assert.False(Directory.Exists(service.DiagnosticsRoot));
    }

    [Fact]
    public async Task OptionsAreNormalizedAndPersisted()
    {
        DeepDebugSessionService service = new(_root);
        await service.UpdateOptionsAsync(enabled: true, frameRetentionMinutes: 999);

        DeepDebugSessionService restored = new(_root);

        Assert.True(restored.Options.Enabled);
        Assert.Equal(120, restored.Options.FrameRetentionMinutes);
    }

    [Fact]
    public async Task RetainAllFrames_RecordsFullOperationPolicy()
    {
        DeepDebugSessionService service = new(_root) { RetainAllFrames = true };
        await service.UpdateOptionsAsync(enabled: true, frameRetentionMinutes: 15);
        DeepDebugScope? scope = await service.OpenSessionAsync(
            "runtime lab",
            new DeepDebugOperationContext("runtime-lab"));
        service.RecordPng(
            PngEncoder.Encode(new RgbImage(1, 1, [1, 2, 3], takeOwnership: true)),
            "runtime-frame");

        await scope!.CompleteAsync("success");

        using ZipArchive archive = ZipFile.OpenRead(service.LastArchivePath!);
        string manifest = await ReadAsync(archive, "manifest.json");
        Assert.Contains("\"frameRetentionMinutes\": 0", manifest, StringComparison.Ordinal);
        Assert.Contains("cover the full operation", manifest, StringComparison.Ordinal);
        Assert.Contains(archive.Entries, entry => entry.FullName.StartsWith("frames/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConcurrentSessionIsRejected()
    {
        DeepDebugSessionService service = new(_root);
        await service.UpdateOptionsAsync(enabled: true, frameRetentionMinutes: 15);
        DeepDebugScope? first = await service.OpenSessionAsync(
            "first",
            new DeepDebugOperationContext("test"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenSessionAsync(
            "second",
            new DeepDebugOperationContext("test")));
        Assert.Single(Directory.EnumerateDirectories(service.DiagnosticsRoot, ".deep-debug-*"));
        await first!.CompleteAsync("success");
    }

    [Fact]
    public async Task SessionIncludesOnlyRegisteredVisualProfileRevision()
    {
        string profileRoot = Path.Combine(_root, "profiles", "wire-test");
        string revision = Path.Combine(profileRoot, "revisions", "20260808T010203000Z-test");
        Directory.CreateDirectory(revision);
        await File.WriteAllTextAsync(
            Path.Combine(revision, "profile.json"),
            $$"""{"source":"C:\\Users\\{{Environment.UserName}}\\profile"}""");
        await File.WriteAllBytesAsync(Path.Combine(revision, "median.pgm"), [80, 53, 10]);
        string locator = Path.Combine(profileRoot, "locator.json");
        await File.WriteAllTextAsync(locator, "{\"bounds\":[1,2,3,4]}");

        DeepDebugSessionService service = new(_root);
        await service.UpdateOptionsAsync(enabled: true, frameRetentionMinutes: 15);
        DeepDebugScope? scope = await service.OpenSessionAsync(
            "profile snapshot",
            new DeepDebugOperationContext("test"));
        service.RecordVisualProfileRevision("wire-test", revision, locator);
        await scope!.CompleteAsync("success");

        using ZipArchive archive = ZipFile.OpenRead(service.LastArchivePath!);
        string prefix = "visual-profiles/wire-test/20260808T010203000Z-test/";
        Assert.Contains(archive.Entries, entry => entry.FullName == prefix + "profile.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == prefix + "median.pgm");
        Assert.Contains(archive.Entries, entry => entry.FullName == prefix + "locator.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == "visual-profiles/index.json");
        string copiedManifest = await ReadAsync(archive, prefix + "profile.json");
        Assert.DoesNotContain(Environment.UserName, copiedManifest, StringComparison.OrdinalIgnoreCase);
        string manifest = await ReadAsync(archive, "manifest.json");
        Assert.Contains("\"visualProfiles\": 1", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingRegisteredLocatorIsReportedInManifest()
    {
        string revision = Path.Combine(_root, "profiles", "wire-test", "revisions", "revision");
        Directory.CreateDirectory(revision);
        await File.WriteAllTextAsync(Path.Combine(revision, "profile.json"), "{}");

        DeepDebugSessionService service = new(_root);
        await service.UpdateOptionsAsync(enabled: true, frameRetentionMinutes: 15);
        DeepDebugScope? scope = await service.OpenSessionAsync(
            "missing locator",
            new DeepDebugOperationContext("test"));
        service.RecordVisualProfileRevision(
            "wire-test",
            revision,
            Path.Combine(_root, "profiles", "wire-test", "locator.json"));
        await scope!.CompleteAsync("success");

        using ZipArchive archive = ZipFile.OpenRead(service.LastArchivePath!);
        string manifest = await ReadAsync(archive, "manifest.json");
        Assert.Contains("Visual locator was unavailable for profile wire-test", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalArchiveCleanupIsIndependentFromAutomaticUploadConsent()
    {
        DeepDebugSessionService service = new(_root);
        Directory.CreateDirectory(service.DiagnosticsRoot);
        for (int index = 0; index < 22; index++)
        {
            string path = Path.Combine(service.DiagnosticsRoot, $"deep-debug-test-{index:D2}.zip");
            await File.WriteAllBytesAsync(path, []);
            File.SetCreationTimeUtc(path, DateTime.UtcNow.AddMinutes(index));
        }

        await service.UpdateOptionsAsync(true, 15, automaticCleanupEnabled: false);
        Assert.Equal(22, Directory.EnumerateFiles(service.DiagnosticsRoot, "deep-debug-*.zip").Count());

        await service.UpdateOptionsAsync(true, 15, automaticCleanupEnabled: true);
        Assert.Equal(20, Directory.EnumerateFiles(service.DiagnosticsRoot, "deep-debug-*.zip").Count());
    }

    private static async Task<string> ReadAsync(ZipArchive archive, string name)
    {
        ZipArchiveEntry entry = Assert.Single(archive.Entries, candidate => candidate.FullName == name);
        await using Stream stream = entry.Open();
        using StreamReader reader = new(stream);
        return await reader.ReadToEndAsync();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // The test runner can release a ZIP handle after disposal returns.
        }
    }
}
