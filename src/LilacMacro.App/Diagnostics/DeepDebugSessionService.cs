using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using LilacMacro.Core.Imaging;
using LilacMacro.Core.Vision;

namespace LilacMacro.App.Diagnostics;

public sealed partial class DeepDebugSessionService
{
    private const int ArchiveLimit = 20;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly JsonSerializerOptions CompactJsonOptions = CreateJsonOptions(writeIndented: false);
    private readonly object _gate = new();
    private readonly string _appDataRoot;
    private readonly string _diagnosticsRoot;
    private readonly DeepDebugOptionsStore _optionsStore;
    private DeepDebugSession? _active;

    public DeepDebugSessionService() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LilacMacro"))
    {
    }

    internal DeepDebugSessionService(string appDataRoot)
    {
        _appDataRoot = Path.GetFullPath(appDataRoot);
        _diagnosticsRoot = Path.Combine(_appDataRoot, "diagnostics");
        _optionsStore = new DeepDebugOptionsStore(_appDataRoot);
        Options = _optionsStore.Load();
    }

    public DeepDebugOptions Options { get; private set; }

    public bool RetainAllFrames { get; set; }

    public bool IsActive
    {
        get { lock (_gate) return _active is not null; }
    }

    public string? LastArchivePath { get; private set; }

    public string DiagnosticsRoot => _diagnosticsRoot;

    public event EventHandler? OptionsChanged;

    public event EventHandler<string>? ArchiveSaved;

    public async Task CompleteActiveAsync(string outcome, Exception? error = null)
    {
        DeepDebugSession? session = ActiveSession();
        if (session is null) return;
        try
        {
            await CompleteAsync(session, outcome, error);
        }
        catch (Exception finalizationError)
        {
            PreserveFinalizationError(session, finalizationError);
        }
    }

    public async Task UpdateOptionsAsync(
        bool enabled,
        int frameRetentionMinutes,
        bool? automaticCleanupEnabled = null)
    {
        Options = new DeepDebugOptions
        {
            Enabled = enabled,
            FrameRetentionMinutes = DeepDebugOptions.NormalizeFrameRetention(frameRetentionMinutes),
            AutomaticCleanupEnabled = automaticCleanupEnabled ?? Options.AutomaticCleanupEnabled,
        };
        await _optionsStore.SaveAsync(Options);
        if (Options.AutomaticCleanupEnabled) PruneOldArchives();
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<DeepDebugScope?> OpenSessionAsync(
        string operation,
        DeepDebugOperationContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(context);
        if (!Options.Enabled) return null;

        string diagnosticsRoot = Path.GetFullPath(_diagnosticsRoot);
        Directory.CreateDirectory(diagnosticsRoot);
        string staging = Path.Combine(diagnosticsRoot, $".deep-debug-{Guid.NewGuid():N}");
        EnsureChildPath(diagnosticsRoot, staging);
        Directory.CreateDirectory(Path.Combine(staging, "frames"));
        Channel<DeepDebugWriteItem> channel = Channel.CreateBounded<DeepDebugWriteItem>(
            new BoundedChannelOptions(32)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
        DeepDebugSession session = new()
        {
            Operation = operation,
            Context = context,
            StartedAtUtc = DateTimeOffset.UtcNow,
            StagingDirectory = staging,
            Channel = channel,
            FrameRetentionMinutes = RetainAllFrames ? 0 : Options.FrameRetentionMinutes,
        };
        lock (_gate)
        {
            if (_active is not null)
            {
                TryDeleteDirectory(staging);
                throw new InvalidOperationException("A deep debug session is already active.");
            }
            _active = session;
        }
        session.WriterTask = Task.Run(() => DeepDebugSessionWriter.WriteAsync(session, CompactJsonOptions));
        try
        {
            await WriteConfigurationAsync(session);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            RecordEvent("configuration", "snapshot_failed", new { Error = error.ToString() });
        }
        RecordEvent("session", "started", new { operation, context.Surface });
        return new DeepDebugScope(this, session);
    }

    public async Task<T> RunOperationAsync<T>(
        string operation,
        DeepDebugOperationContext context,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        DeepDebugScope? scope = await OpenSessionAsync(operation, context);
        try
        {
            T result = await action(cancellationToken);
            if (scope is not null) await scope.CompleteAsync("success");
            return result;
        }
        catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested)
        {
            if (scope is not null) await scope.CompleteAsync("canceled", error);
            throw;
        }
        catch (Exception error)
        {
            if (scope is not null) await scope.CompleteAsync("error", error);
            throw;
        }
    }

    public void RecordEvent(string category, string action, object? data = null)
    {
        PublishObservation(category, action, data, null);
        DeepDebugSession? session = ActiveSession();
        if (session is null) return;
        Interlocked.Increment(ref session.EventCount);
        Enqueue(session, category, action, data, null, null);
    }

    public void RecordRuntimeLog(string message) =>
        RecordEvent("macro", "log", new { Message = message });

    public void RecordInput(string action, object? data = null)
    {
        DeepDebugSession? session = ActiveSession();
        if (session is not null) Interlocked.Increment(ref session.InputEventCount);
        RecordEvent("input", action, data);
    }

    internal void RecordVisualProfileRevision(
        string profileId,
        string revisionDirectory,
        string? locatorPath = null)
    {
        DeepDebugSession? session = ActiveSession();
        if (session is null) return;
        try
        {
            string revision = Path.GetFullPath(revisionDirectory);
            string key = $"{profileId}\0{Path.GetFileName(revision)}";
            if (session.VisualProfiles.Count >= 64 && !session.VisualProfiles.ContainsKey(key)) return;
            if (!session.VisualProfiles.TryAdd(
                    key,
                    new DeepDebugVisualProfileReference(profileId, revision, locatorPath))) return;
            RecordEvent("vision", "profile_snapshot_registered", new
            {
                ProfileId = profileId,
                Revision = Path.GetFileName(revision),
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            RecordEvent("vision", "profile_snapshot_skipped", new
            {
                ProfileId = profileId,
                Error = error.Message,
            });
        }
    }

    public void RecordPng(ReadOnlySpan<byte> png, string source, object? data = null)
    {
        if (FrameRecorded is not null) PublishFrameObservation(source, data, png.ToArray());
        DeepDebugSession? session = ActiveSession();
        if (session is null) return;
        int index = Interlocked.Increment(ref session.ArtifactCount);
        string path = $"frames/frame-{index:D9}-{SafeName(source)}.png";
        Enqueue(session, "frame", source, data, path, png.ToArray());
    }

    public void RecordGrayImage(GrayImage image, string source, object? data = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        byte[] rgb = new byte[checked(image.Width * image.Height * 3)];
        ReadOnlySpan<byte> gray = image.Pixels.Span;
        for (int index = 0; index < gray.Length; index++)
        {
            int target = index * 3;
            rgb[target] = gray[index];
            rgb[target + 1] = gray[index];
            rgb[target + 2] = gray[index];
        }
        RecordPng(PngEncoder.Encode(new RgbImage(image.Width, image.Height, rgb, true)), source, data);
    }

    internal async Task CompleteAsync(DeepDebugSession session, string outcome, Exception? error)
    {
        if (!ReferenceEquals(ActiveSession(), session)) return;
        RecordEvent("session", "finished", new
        {
            Outcome = outcome,
            Error = error is null ? null : DeepDebugRedactor.Redact(error.ToString()),
        });
        lock (_gate)
        {
            if (ReferenceEquals(_active, session)) _active = null;
        }
        session.Channel.Writer.TryComplete();
        try
        {
            await session.WriterTask;
        }
        catch (Exception writerError)
        {
            session.WriterFailure ??= writerError;
        }

        DateTimeOffset completed = DateTimeOffset.UtcNow;
        DeepDebugSessionWriter.PruneExpiredArtifacts(session, completed);
        int visualProfiles = WriteVisualProfileSnapshots(session);
        await CopyLatestCrashLogAsync(session.StagingDirectory);
        await WriteReadmeAsync(session.StagingDirectory);
        await WriteJsonAsync(
            Path.Combine(session.StagingDirectory, "manifest.json"),
            new DeepDebugManifest(
                1,
                session.Operation,
                outcome,
                GetVersion(),
                session.StartedAtUtc,
                completed,
                completed - session.StartedAtUtc,
                Volatile.Read(ref session.ArtifactCount),
                Volatile.Read(ref session.EventCount),
                Volatile.Read(ref session.InputEventCount),
                session.FrameRetentionMinutes,
                session.RetainedFrames.Count,
                Volatile.Read(ref session.DiscardedArtifactCount),
                visualProfiles,
                session.RetainsAllFrames
                    ? "events.jsonl, timeline.md, and already-acquired PNG evidence cover the full operation. Visual profiles contain only immutable revisions consulted by this run."
                    : "events.jsonl and timeline.md cover the full operation. PNG evidence uses already-acquired captures and retains only the final rolling window. Visual profiles contain only immutable revisions consulted by this run.",
                session.WriterFailure is null ? null : DeepDebugRedactor.Redact(session.WriterFailure.ToString()),
                error is null ? null : DeepDebugRedactor.Redact(error.ToString()),
                "Private-server links, Discord webhooks, Windows usernames, and profile paths are redacted. Captured Roblox pixels can still contain personal game data."));
        string archive = CreateArchive(session);
        LastArchivePath = archive;
        TryDeleteDirectory(session.StagingDirectory);
        if (Options.AutomaticCleanupEnabled) PruneOldArchives();
        try
        {
            ArchiveSaved?.Invoke(this, archive);
        }
        catch (Exception)
        {
            // A closed UI observer must not turn a successfully saved archive into an operation failure.
        }
    }

    private void Enqueue(
        DeepDebugSession session,
        string category,
        string action,
        object? data,
        string? artifactPath,
        byte[]? artifactBytes)
    {
        if (session.WriterFailure is not null) return;
        long sequence = Interlocked.Increment(ref session.Sequence);
        DeepDebugWriteItem item = new(
            sequence,
            DateTimeOffset.UtcNow,
            category,
            action,
            data,
            artifactPath,
            artifactBytes);
        try
        {
            session.Channel.Writer.WriteAsync(item).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception error) when (error is ChannelClosedException or InvalidOperationException)
        {
            session.WriterFailure ??= error;
        }
    }

    private async Task WriteConfigurationAsync(DeepDebugSession session)
    {
        string root = Path.Combine(session.StagingDirectory, "configuration");
        await WriteJsonAsync(Path.Combine(root, "operation-context.json"), session.Context);
        await WriteJsonAsync(Path.Combine(root, "deep-debug-options.json"), Options);
        await WriteJsonAsync(Path.Combine(root, "environment.json"), new
        {
            AppVersion = GetVersion(),
            OperatingSystem = Environment.OSVersion.VersionString,
            Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            ProcessArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessId = Environment.ProcessId,
            CapturedAtUtc = DateTimeOffset.UtcNow,
        });
    }

    private async Task CopyLatestCrashLogAsync(string staging)
    {
        string source = Path.Combine(_appDataRoot, "logs", "latest-crash.txt");
        if (!File.Exists(source)) return;
        try
        {
            string text = await File.ReadAllTextAsync(source);
            await File.WriteAllTextAsync(
                Path.Combine(staging, "latest-crash-sanitized.txt"),
                DeepDebugRedactor.Redact(text),
                new UTF8Encoding(false));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            await File.WriteAllTextAsync(
                Path.Combine(staging, "latest-crash-copy-error.txt"),
                DeepDebugRedactor.Redact(error.Message));
        }
    }

    private static int WriteVisualProfileSnapshots(DeepDebugSession session)
    {
        try
        {
            return DeepDebugVisualProfileSnapshotWriter.Write(session);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            File.WriteAllText(
                Path.Combine(session.StagingDirectory, "visual-profile-copy-error.txt"),
                DeepDebugRedactor.Redact(error.ToString()));
            return 0;
        }
    }

    private static Task WriteReadmeAsync(string staging) => File.WriteAllTextAsync(
        Path.Combine(staging, "README.md"),
        "# LilacMacro deep debug session\n\n" +
        "Start with `manifest.json`, then read `timeline.md` or machine-readable `events.jsonl`. " +
        "Frame links point to decision-time PNG evidence. `visual-profiles/` contains the exact bounded profile revisions consulted by this run. " +
        "Coordinates are Roblox client-relative half-open rectangles.\n",
        new UTF8Encoding(false));

    private async Task WriteJsonAsync<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = JsonSerializer.Serialize(value, JsonOptions);
        await File.WriteAllTextAsync(path, DeepDebugRedactor.Redact(json), new UTF8Encoding(false));
    }

    private string CreateArchive(DeepDebugSession session)
    {
        string name = $"deep-debug-{SafeName(session.Operation)}-{session.StartedAtUtc.ToLocalTime():yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip";
        string archive = Path.Combine(_diagnosticsRoot, name);
        string temporary = Path.Combine(_diagnosticsRoot, $".{name}.tmp");
        EnsureChildPath(_diagnosticsRoot, archive);
        try
        {
            ZipFile.CreateFromDirectory(session.StagingDirectory, temporary, CompressionLevel.NoCompression, false);
            File.Move(temporary, archive, false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return archive;
    }

    private void PruneOldArchives()
    {
        try
        {
            FileInfo[] archives = new DirectoryInfo(_diagnosticsRoot)
                .EnumerateFiles("deep-debug-*.zip")
                .OrderByDescending(file => file.CreationTimeUtc)
                .ToArray();
            foreach (FileInfo archive in archives.Skip(ArchiveLimit)) archive.Delete();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Archive creation succeeded; retention cleanup can retry next session.
        }
    }

    private DeepDebugSession? ActiveSession()
    {
        lock (_gate) return _active;
    }

    private static string GetVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

    private static string SafeName(string value)
    {
        HashSet<char> invalid = Path.GetInvalidFileNameChars().ToHashSet();
        string safe = new(value.Trim().ToLowerInvariant().Select(character =>
            invalid.Contains(character) || char.IsWhiteSpace(character) ? '-' : character).ToArray());
        while (safe.Contains("--", StringComparison.Ordinal)) safe = safe.Replace("--", "-", StringComparison.Ordinal);
        safe = safe.Trim('-', '.');
        return safe.Length == 0 ? "operation" : safe[..Math.Min(safe.Length, 48)];
    }

    private static void EnsureChildPath(string root, string path)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Deep debug output resolved outside the diagnostics folder.");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    internal void PreserveFinalizationError(DeepDebugSession session, Exception error)
    {
        try
        {
            Directory.CreateDirectory(session.StagingDirectory);
            File.WriteAllText(
                Path.Combine(session.StagingDirectory, "finalization-error.txt"),
                DeepDebugRedactor.Redact(error.ToString()));
        }
        catch (Exception ignored) when (ignored is IOException or UnauthorizedAccessException)
        {
            // The primary operation must not fail because diagnostics could not finalize.
        }
    }

    private static JsonSerializerOptions CreateJsonOptions(bool writeIndented = true) => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = writeIndented,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed class DeepDebugScope
{
    private readonly DeepDebugSessionService _service;
    private readonly DeepDebugSession _session;
    private int _completed;

    internal DeepDebugScope(DeepDebugSessionService service, DeepDebugSession session)
    {
        _service = service;
        _session = session;
    }

    public async Task CompleteAsync(string outcome, Exception? error = null)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0) return;
        try
        {
            await _service.CompleteAsync(_session, outcome, error);
        }
        catch (Exception finalizationError)
        {
            _service.PreserveFinalizationError(_session, finalizationError);
        }
    }
}
