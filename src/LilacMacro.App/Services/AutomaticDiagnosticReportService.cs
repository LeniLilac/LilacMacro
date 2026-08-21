using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using LilacMacro.App.Notifications;
using LilacMacro.App.Runtime;
using LilacMacro.Core.Services;
using LilacMacro.Runtime.Services;

namespace LilacMacro.App.Diagnostics;

internal sealed class AutomaticDiagnosticReportService : IAsyncDisposable
{
    private sealed record Consent(long Generation, CancellationToken Token);
    internal const long MaximumLightArchiveBytes = 80L * 1024 * 1024;
    internal const string PendingDeepDebugDeleteSuffix = ".uploaded-delete-pending";
    private static readonly TimeSpan LightReportCooldown = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StaleTemporaryAge = TimeSpan.FromHours(6);
    private static string AutomaticReportRoot =>
        Path.Combine(Path.GetTempPath(), "LilacMacro", "automatic-reports");
    private readonly DeepDebugSessionService _deepDebug;
    private readonly MacroOwnerState _ownerState;
    private readonly DiagnosticInstallationStore _installation;
    private readonly IDiagnosticUploadTransport _transport;
    private readonly LightDiagnosticBuffer _buffer = new();
    private readonly SemaphoreSlim _uploadGate = new(1, 1);
    private readonly object _choiceGate = new();
    private readonly List<CancellationTokenSource> _retiredChoiceCancellations = [];
    private CancellationTokenSource _choiceCancellation = new();
    private long _choiceGeneration;
    private readonly object _taskGate = new();
    private readonly List<Task> _tasks = [];
    private DateTimeOffset _lastLightReportAt;
    private bool _frameSubscribed;

    internal AutomaticDiagnosticReportService(
        DeepDebugSessionService deepDebug,
        MacroOwnerState ownerState,
        DiagnosticInstallationStore installation,
        IDiagnosticUploadTransport transport)
    {
        _deepDebug = deepDebug;
        _ownerState = ownerState;
        _installation = installation;
        _transport = transport;
        _deepDebug.ObservationRecorded += DeepDebug_OnObservationRecorded;
        _deepDebug.ArchiveSaved += DeepDebug_OnArchiveSaved;
        _ownerState.PrivacyOptionsChanged += OwnerState_OnPrivacyOptionsChanged;
        UpdateFrameSubscription();
        ScavengeStaleTemporaryReports();
        RetryPendingDeepDebugDeletes();
    }

    public async ValueTask DisposeAsync()
    {
        _deepDebug.ObservationRecorded -= DeepDebug_OnObservationRecorded;
        if (_frameSubscribed) _deepDebug.FrameRecorded -= DeepDebug_OnFrameRecorded;
        _deepDebug.ArchiveSaved -= DeepDebug_OnArchiveSaved;
        _ownerState.PrivacyOptionsChanged -= OwnerState_OnPrivacyOptionsChanged;
        lock (_choiceGate)
        {
            _choiceCancellation.Cancel();
            foreach (CancellationTokenSource retired in _retiredChoiceCancellations) retired.Cancel();
        }
        Task[] tasks;
        lock (_taskGate) tasks = _tasks.ToArray();
        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        RetryPendingDeepDebugDeletes();
        lock (_choiceGate)
        {
            _choiceCancellation.Dispose();
            foreach (CancellationTokenSource retired in _retiredChoiceCancellations) retired.Dispose();
            _retiredChoiceCancellations.Clear();
        }
        _uploadGate.Dispose();
    }

    private void DeepDebug_OnObservationRecorded(object? sender, DeepDebugObservation observation)
    {
        LightDiagnosticSnapshot? snapshot = null;
        Consent? consent = null;
        lock (_choiceGate)
        {
            if (!_ownerState.HasAcceptedCurrentPrivacyChoices
                || !_ownerState.AutomaticErrorReportsEnabled
                || !_ownerState.AreAutomaticReportsDurablyEnabled()) return;
            _buffer.Capture(observation);
            if (_deepDebug.IsActive || !IsError(observation)) return;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (now - _lastLightReportAt < LightReportCooldown) return;
            _lastLightReportAt = now;
            snapshot = _buffer.SnapshotAndClear();
            consent = CurrentConsentLocked();
        }
        Track(SendLightReportAsync(snapshot, consent));
    }

    private void DeepDebug_OnFrameRecorded(object? sender, DeepDebugObservation observation)
    {
        lock (_choiceGate)
        {
            if (_ownerState.HasAcceptedCurrentPrivacyChoices
                && _ownerState.AutomaticErrorReportsEnabled
                && _ownerState.AreAutomaticReportsDurablyEnabled()) _buffer.Capture(observation);
        }
    }

    private void DeepDebug_OnArchiveSaved(object? sender, string archivePath)
    {
        Consent? consent;
        lock (_choiceGate)
        {
            if (!_ownerState.HasAcceptedCurrentPrivacyChoices
                || !_ownerState.AutomaticErrorReportsEnabled
                || !_ownerState.AreAutomaticReportsDurablyEnabled()) return;
            consent = CurrentConsentLocked();
        }
        Track(SendArchiveAsync(archivePath, DiagnosticArchiveKind.DeepDebug, deleteAfterSuccess: true, consent));
    }

    private void OwnerState_OnPrivacyOptionsChanged(object? sender, EventArgs eventArgs)
    {
        UpdateFrameSubscription();
        if (_ownerState.AutomaticErrorReportsEnabled) return;
        lock (_choiceGate)
        {
            _choiceCancellation.Cancel();
            _retiredChoiceCancellations.Add(_choiceCancellation);
            _choiceCancellation = new CancellationTokenSource();
            _choiceGeneration++;
            _buffer.Clear();
        }
    }

    private void UpdateFrameSubscription()
    {
        bool shouldSubscribe = _ownerState.AutomaticErrorReportsEnabled;
        if (shouldSubscribe == _frameSubscribed) return;
        if (shouldSubscribe) _deepDebug.FrameRecorded += DeepDebug_OnFrameRecorded;
        else _deepDebug.FrameRecorded -= DeepDebug_OnFrameRecorded;
        _frameSubscribed = shouldSubscribe;
    }

    private void Track(Task task)
    {
        lock (_taskGate) _tasks.Add(task);
        _ = task.ContinueWith(
            completed =>
            {
                _ = completed.Exception;
                lock (_taskGate) _tasks.Remove(completed);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task SendLightReportAsync(LightDiagnosticSnapshot snapshot, Consent consent)
    {
        if (snapshot.Events.Count == 0) return;
        string? archive = null;
        try
        {
            archive = await CreateLightArchiveAsync(snapshot, consent.Token).ConfigureAwait(false);
            await SendArchiveAsync(
                archive,
                DiagnosticArchiveKind.LiveDebug,
                deleteAfterSuccess: false,
                consent).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidDataException or JsonException or TaskCanceledException)
        {
            if (!consent.Token.IsCancellationRequested)
                AppToastService.ShowError("AUTOMATIC REPORT NOT CREATED", "The temporary diagnostic report could not be prepared; automation can continue.");
        }
        finally
        {
            if (archive is not null) TryDeleteFile(archive);
        }
    }

    private async Task SendArchiveAsync(
        string archivePath,
        DiagnosticArchiveKind kind,
        bool deleteAfterSuccess,
        Consent consent)
    {
        if (!IsCurrent(consent)
            || !await _ownerState.AreAutomaticReportsDurablyEnabledAsync(consent.Token)
                .ConfigureAwait(false)) return;
        await _uploadGate.WaitAsync(consent.Token).ConfigureAwait(false);
        try
        {
            if (!IsCurrent(consent)
                || !await _ownerState.AreAutomaticReportsDurablyEnabledAsync(consent.Token)
                    .ConfigureAwait(false)) return;
            Guid installId = await _installation.GetOrCreateAsync(consent.Token).ConfigureAwait(false);
            if (!IsCurrent(consent)
                || !await _ownerState.AreAutomaticReportsDurablyEnabledAsync(consent.Token)
                    .ConfigureAwait(false)) return;
            await _transport.UploadAsync(
                archivePath,
                kind,
                BuildVersion(),
                installId,
                progress: null,
                consent.Token).ConfigureAwait(false);
            if (deleteAfterSuccess) DeleteUploadedDeepDebugOrMarkPending(archivePath);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException
            or UnauthorizedAccessException or InvalidDataException or JsonException
            or TaskCanceledException)
        {
            if (!consent.Token.IsCancellationRequested)
                AppToastService.ShowError("AUTOMATIC REPORT NOT SENT", "The diagnostic service was unavailable; automation can continue.");
        }
        finally
        {
            _uploadGate.Release();
        }
    }

    private async Task<string> CreateLightArchiveAsync(
        LightDiagnosticSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        string root = AutomaticReportRoot;
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Automatic report root cannot be a reparse point.");
        string id = Guid.NewGuid().ToString("N");
        string staging = Path.Combine(root, $".live-debug-{id}");
        string archive = Path.Combine(root, $"live-debug-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{id}.zip");
        Directory.CreateDirectory(Path.Combine(staging, "frames"));
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(staging, "manifest.json"),
                JsonSerializer.Serialize(CreateManifest(snapshot), new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken).ConfigureAwait(false);
            await File.WriteAllLinesAsync(
                Path.Combine(staging, "events.jsonl"),
                snapshot.Events.Select(item => JsonSerializer.Serialize(item)),
                cancellationToken).ConfigureAwait(false);
            int index = 0;
            foreach (LightDiagnosticFrame frame in snapshot.Frames)
            {
                string fileName = $"frame-{++index:D3}-{frame.Source}.png";
                await File.WriteAllBytesAsync(
                    Path.Combine(staging, "frames", fileName),
                    frame.PngBytes,
                    cancellationToken).ConfigureAwait(false);
            }
            ZipFile.CreateFromDirectory(staging, archive, CompressionLevel.Fastest, includeBaseDirectory: false);
            if (new FileInfo(archive).Length > MaximumLightArchiveBytes)
                throw new InvalidDataException("Automatic diagnostic report exceeded its size bound.");
            return archive;
        }
        catch
        {
            TryDeleteFile(archive);
            throw;
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private object CreateManifest(LightDiagnosticSnapshot snapshot) => new
    {
        SchemaVersion = 1,
        Kind = "automatic-light-diagnostic",
        AppVersion = BuildVersion(),
        CreatedAtUtc = DateTimeOffset.UtcNow,
        EventCount = snapshot.Events.Count,
        FrameCount = snapshot.Frames.Count,
        Limits = new { MaximumFrames = LightDiagnosticBuffer.MaximumFrames, MaximumArchiveBytes = MaximumLightArchiveBytes },
        Configuration = new
        {
            _ownerState.LayoutProfile,
            _ownerState.MinimizeBehavior,
            _ownerState.ThemeMode,
            _ownerState.ColorTheme,
            PlanCount = _ownerState.Plans.Count,
        },
        Environment = new
        {
            OperatingSystem = Environment.OSVersion.VersionString,
            Environment.ProcessorCount,
            ProcessArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        },
        Privacy = "No private-server link, Discord webhook, user ID, file path, OCR text, or free-form exception text is included. Captured Roblox pixels may contain personal game data.",
    };

    private static bool IsError(DeepDebugObservation observation) =>
        observation.Category is "macro" or "application"
        && observation.Action is "runtime_error" or "unhandled_exception";

    private Consent CurrentConsentLocked() => new(_choiceGeneration, _choiceCancellation.Token);

    private bool IsCurrent(Consent consent)
    {
        lock (_choiceGate)
        {
            return _ownerState.HasAcceptedCurrentPrivacyChoices
                && _ownerState.AutomaticErrorReportsEnabled
                && consent.Generation == _choiceGeneration
                && !consent.Token.IsCancellationRequested;
        }
    }

    private static string BuildVersion() =>
        typeof(AutomaticDiagnosticReportService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private static bool TryDeleteFile(string path)
    {
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                return true;
            }
            catch (Exception exception) when (
                attempt < 3 && exception is (IOException or UnauthorizedAccessException))
            {
                Thread.Sleep(25 * attempt);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        return false;
    }

    private void DeleteUploadedDeepDebugOrMarkPending(string archivePath)
    {
        string? validatedArchive = ValidateDeepDebugArchivePath(archivePath);
        if (validatedArchive is null) return;
        string marker = validatedArchive + PendingDeepDebugDeleteSuffix;
        if (TryDeleteFile(validatedArchive))
        {
            TryDeleteFile(marker);
            return;
        }

        try
        {
            using FileStream ignored = new(
                marker,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.WriteThrough);
            ignored.SetLength(0);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A later diagnostics retention pass can still remove the successfully sent archive.
        }
    }

    private void RetryPendingDeepDebugDeletes()
    {
        try
        {
            DirectoryInfo root = new(_deepDebug.DiagnosticsRoot);
            if (!root.Exists || (root.Attributes & FileAttributes.ReparsePoint) != 0) return;
            foreach (FileInfo marker in root
                .EnumerateFiles($"deep-debug-*.zip{PendingDeepDebugDeleteSuffix}", SearchOption.TopDirectoryOnly)
                .Take(64))
            {
                if ((marker.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                string markerPath = Path.GetFullPath(marker.FullName);
                string archivePath = markerPath[..^PendingDeepDebugDeleteSuffix.Length];
                string? validatedArchive = ValidateDeepDebugArchivePath(archivePath);
                if (validatedArchive is null) continue;
                if (TryDeleteFile(validatedArchive)) TryDeleteFile(markerPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Cleanup is bounded and retried at the next startup or orderly close.
        }
    }

    private string? ValidateDeepDebugArchivePath(string archivePath)
    {
        string root = Path.GetFullPath(_deepDebug.DiagnosticsRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullPath;
        try { fullPath = Path.GetFullPath(archivePath); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or PathTooLongException)
        {
            return null;
        }
        if (!string.Equals(Path.GetDirectoryName(fullPath), root, StringComparison.OrdinalIgnoreCase))
            return null;
        string fileName = Path.GetFileName(fullPath);
        return fileName.StartsWith("deep-debug-", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : null;
    }

    private static void TryDeleteDirectory(string path)
    {
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception exception) when (
                attempt < 3 && exception is (IOException or UnauthorizedAccessException))
            {
                Thread.Sleep(25 * attempt);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return;
            }
        }
    }

    private static void ScavengeStaleTemporaryReports()
    {
        try
        {
            DirectoryInfo root = new(AutomaticReportRoot);
            if (!root.Exists || (root.Attributes & FileAttributes.ReparsePoint) != 0) return;
            DateTime cutoff = DateTime.UtcNow - StaleTemporaryAge;
            foreach (FileInfo file in root.EnumerateFiles("live-debug-*.zip").Take(256))
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) == 0 && file.LastWriteTimeUtc < cutoff)
                    TryDeleteFile(file.FullName);
            }
            foreach (DirectoryInfo directory in root.EnumerateDirectories(".live-debug-*").Take(256))
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) == 0
                    && directory.LastWriteTimeUtc < cutoff)
                {
                    TryDeleteDirectory(directory.FullName);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Startup cleanup is bounded and best effort; active reporting still deletes on every exit.
        }
    }
}
