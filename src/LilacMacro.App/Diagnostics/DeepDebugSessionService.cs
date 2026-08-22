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
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly JsonSerializerOptions CompactJsonOptions = CreateJsonOptions(writeIndented: false);
    private readonly object _gate = new();
    private readonly string _appDataRoot;
    private readonly string _diagnosticsRoot;
    private readonly DeepDebugConfigurationStore _configurationStore;
    private readonly DeepDebugArchiveFinalizer _archiveFinalizer;
    private readonly DeepDebugArchiveLimits _limits;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly IDeepDebugFrameCodec _frameCodec;
    private readonly Dictionary<string, DeepDebugFrameCaptureProvider> _frameCaptureProviders =
        new(StringComparer.OrdinalIgnoreCase);
    private DeepDebugSession? _active;

    public DeepDebugSessionService() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LilacMacro"))
    {
    }

    internal DeepDebugSessionService(
        string appDataRoot,
        string? diagnosticsRoot = null,
        Func<string, long>? availableFreeBytes = null,
        DeepDebugArchiveLimits? limits = null,
        Func<DateTimeOffset>? utcNow = null,
        IDeepDebugFrameCodec? frameCodec = null)
    {
        _appDataRoot = Path.GetFullPath(appDataRoot);
        _diagnosticsRoot = Path.GetFullPath(
            diagnosticsRoot ?? Path.Combine(_appDataRoot, "diagnostics"));
        _configurationStore = new DeepDebugConfigurationStore(
            _appDataRoot,
            _diagnosticsRoot,
            availableFreeBytes);
        _limits = limits ?? DeepDebugArchiveLimits.Production;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _frameCodec = frameCodec ?? new DeepDebugFrameCodec(_diagnosticsRoot);
        _archiveFinalizer = new DeepDebugArchiveFinalizer(
            _appDataRoot,
            _diagnosticsRoot,
            JsonOptions);
        Options = _configurationStore.Load();
        _configurationStore.PruneArchives(Options);
        IsTemporarilyPausedByStorage = _configurationStore.StorageState(Options).CapturePaused;
    }

    public DeepDebugOptions Options { get; private set; }

    public bool IsTemporarilyPausedByStorage { get; private set; }

    public bool IsActive
    {
        get { lock (_gate) return _active is not null; }
    }

    public string? LastArchivePath { get; private set; }

    public string DiagnosticsRoot => _diagnosticsRoot;

    public event EventHandler? OptionsChanged;

    public event EventHandler<string>? ArchiveSaved;

    internal event EventHandler<string>? AutomaticReportArchiveSaved;

    internal IDisposable RegisterFrameCaptureProvider(
        string surface,
        Func<CancellationToken, Task> capture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surface);
        ArgumentNullException.ThrowIfNull(capture);
        DeepDebugFrameCaptureProvider provider = new(surface, capture);
        lock (_gate) _frameCaptureProviders[surface] = provider;
        return new Registration(this, provider);
    }

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
        bool? enabled = null,
        int? maximumArchiveStorageGiB = null)
    {
        Options = await _configurationStore.UpdateAsync(
            enabled,
            maximumArchiveStorageGiB);
        _configurationStore.PruneArchives(Options);
        IsTemporarilyPausedByStorage = _configurationStore.StorageState(Options).CapturePaused;
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void RefreshOptions() => Options = _configurationStore.Load();

    public async Task<DeepDebugScope?> OpenSessionAsync(
        string operation,
        DeepDebugOperationContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(context);
        RefreshOptions();
        DeepDebugStorageState storage = _configurationStore.StorageState(Options);
        IsTemporarilyPausedByStorage = storage.CapturePaused;
        if (!Options.Enabled || IsTemporarilyPausedByStorage) return null;

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
            StartedAtUtc = _utcNow(),
            StagingDirectory = staging,
            Channel = channel,
            Limits = _limits,
            Evidence = new DeepDebugEvidenceRetention(),
            FrameCodec = _frameCodec,
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
        DeepDebugFrameCaptureProvider? provider = GetFrameCaptureProvider(context.Surface);
        session.FrameCaptureLoop = provider is null
            ? null
            : new DeepDebugFrameCaptureLoop(
                this,
                provider.Capture);
        session.FrameCaptureLoop?.Start();
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
        DeepDebugStorageState storage = _configurationStore.StorageState(Options);
        if (storage.CapturePaused)
        {
            bool newlyPaused = !IsTemporarilyPausedByStorage;
            IsTemporarilyPausedByStorage = true;
            if (newlyPaused)
                RecordEvent("diagnostic", "capture_paused_low_disk", new { storage.FreeBytes });
            return;
        }
        IsTemporarilyPausedByStorage = false;
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
        if (session.FrameCaptureLoop is { } frameCaptureLoop)
        {
            await frameCaptureLoop.StopAsync();
            session.FrameCaptureLoop = null;
        }
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

        DateTimeOffset completedAtUtc = _utcNow();
        string archive = await Task.Run(() => _archiveFinalizer.FinalizeAsync(
            session,
            outcome,
            error,
            completedAtUtc));
        LastArchivePath = archive;
        TryDeleteDirectory(session.StagingDirectory);
        _configurationStore.PruneArchives(Options);
        NotifyArchiveSaved(ArchiveSaved, archive);
        if (session.Evidence.WindowCount > 0)
            NotifyArchiveSaved(AutomaticReportArchiveSaved, archive);
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
            _utcNow(),
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
        await WriteJsonAsync(
            Path.Combine(root, "operation-context.json"),
            session.Context,
            session.Limits.ConfigurationBytes * 3 / 4);
        await WriteJsonAsync(
            Path.Combine(root, "deep-debug-options.json"),
            Options,
            session.Limits.ConfigurationBytes / 8);
        await WriteJsonAsync(Path.Combine(root, "environment.json"), new
        {
            AppVersion = GetVersion(),
            OperatingSystem = Environment.OSVersion.VersionString,
            Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            ProcessArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessId = Environment.ProcessId,
            CapturedAtUtc = _utcNow(),
        }, session.Limits.ConfigurationBytes / 8);
    }

    private async Task WriteJsonAsync<T>(string path, T value, long maximumBytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = DeepDebugRedactor.Redact(JsonSerializer.Serialize(value, JsonOptions));
        if (Encoding.UTF8.GetByteCount(json) > maximumBytes)
        {
            json = JsonSerializer.Serialize(new
            {
                Truncated = true,
                Reason = "Configuration artifact exceeded its Deep Debug safety bound.",
            }, JsonOptions);
        }
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false));
    }

    private DeepDebugSession? ActiveSession()
    {
        lock (_gate) return _active;
    }

    private void NotifyArchiveSaved(EventHandler<string>? handlers, string archive)
    {
        if (handlers is null) return;
        foreach (EventHandler<string> handler in handlers.GetInvocationList())
        {
            try { handler(this, archive); }
            catch (Exception)
            {
                // A closed UI or optional network observer cannot change archive finalization.
            }
        }
    }

    private DeepDebugFrameCaptureProvider? GetFrameCaptureProvider(string surface)
    {
        lock (_gate)
        {
            return _frameCaptureProviders.TryGetValue(surface, out DeepDebugFrameCaptureProvider? provider)
                ? provider
                : null;
        }
    }

    private void RemoveFrameCaptureProvider(DeepDebugFrameCaptureProvider provider)
    {
        lock (_gate)
        {
            if (_frameCaptureProviders.TryGetValue(provider.Surface, out DeepDebugFrameCaptureProvider? current) &&
                ReferenceEquals(current, provider))
            {
                _frameCaptureProviders.Remove(provider.Surface);
            }
        }
    }

    private sealed class Registration(
        DeepDebugSessionService service,
        DeepDebugFrameCaptureProvider provider) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                service.RemoveFrameCaptureProvider(provider);
        }
    }
    internal static string GetVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

    internal static string SafeName(string value)
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
