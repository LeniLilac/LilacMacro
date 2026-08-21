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

    internal const string PendingDeepDebugDeleteSuffix = ".uploaded-delete-pending";
    private readonly DeepDebugSessionService _deepDebug;
    private readonly MacroOwnerState _ownerState;
    private readonly DiagnosticInstallationStore _installation;
    private readonly IDiagnosticUploadTransport _transport;
    private readonly SemaphoreSlim _uploadGate = new(1, 1);
    private readonly object _choiceGate = new();
    private readonly List<CancellationTokenSource> _retiredCancellations = [];
    private CancellationTokenSource _choiceCancellation = new();
    private long _choiceGeneration;
    private readonly object _taskGate = new();
    private readonly List<Task> _tasks = [];

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
        _deepDebug.AutomaticReportArchiveSaved += DeepDebug_OnArchiveSaved;
        _deepDebug.OptionsChanged += DeepDebug_OnOptionsChanged;
        _ownerState.PrivacyOptionsChanged += OwnerState_OnPrivacyOptionsChanged;
        RetryPendingDeepDebugDeletes();
    }

    public async ValueTask DisposeAsync()
    {
        _deepDebug.AutomaticReportArchiveSaved -= DeepDebug_OnArchiveSaved;
        _deepDebug.OptionsChanged -= DeepDebug_OnOptionsChanged;
        _ownerState.PrivacyOptionsChanged -= OwnerState_OnPrivacyOptionsChanged;
        RevokeCurrentGeneration();
        Task[] tasks;
        lock (_taskGate) tasks = _tasks.ToArray();
        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        RetryPendingDeepDebugDeletes();
        lock (_choiceGate)
        {
            _choiceCancellation.Dispose();
            foreach (CancellationTokenSource retired in _retiredCancellations) retired.Dispose();
            _retiredCancellations.Clear();
        }
        _uploadGate.Dispose();
    }

    private void DeepDebug_OnArchiveSaved(object? sender, string archivePath)
    {
        Consent? consent;
        lock (_choiceGate)
        {
            if (!CanSend()) return;
            consent = new(_choiceGeneration, _choiceCancellation.Token);
        }
        Track(SendArchiveAsync(archivePath, consent));
    }

    private void DeepDebug_OnOptionsChanged(object? sender, EventArgs eventArgs)
    {
        if (!_deepDebug.Options.Enabled) RevokeCurrentGeneration();
    }

    private void OwnerState_OnPrivacyOptionsChanged(object? sender, EventArgs eventArgs)
    {
        if (!_ownerState.AutomaticErrorReportsEnabled) RevokeCurrentGeneration();
    }

    private bool CanSend() =>
        _deepDebug.Options.Enabled &&
        !_deepDebug.IsTemporarilyPausedByStorage &&
        _ownerState.HasAcceptedCurrentPrivacyChoices &&
        _ownerState.AutomaticErrorReportsEnabled &&
        _ownerState.AreAutomaticReportsDurablyEnabled();

    private void RevokeCurrentGeneration()
    {
        lock (_choiceGate)
        {
            _choiceCancellation.Cancel();
            _retiredCancellations.Add(_choiceCancellation);
            _choiceCancellation = new CancellationTokenSource();
            _choiceGeneration++;
        }
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

    private async Task SendArchiveAsync(string archivePath, Consent consent)
    {
        if (!IsCurrent(consent) ||
            !await _ownerState.AreAutomaticReportsDurablyEnabledAsync(consent.Token)
                .ConfigureAwait(false)) return;
        await _uploadGate.WaitAsync(consent.Token).ConfigureAwait(false);
        try
        {
            if (!IsCurrent(consent) ||
                !await _ownerState.AreAutomaticReportsDurablyEnabledAsync(consent.Token)
                    .ConfigureAwait(false)) return;
            Guid installId = await _installation.GetOrCreateAsync(consent.Token).ConfigureAwait(false);
            if (!IsCurrent(consent)) return;
            await _transport.UploadAsync(
                archivePath,
                DiagnosticArchiveKind.DeepDebug,
                BuildVersion(),
                installId,
                progress: null,
                consent.Token).ConfigureAwait(false);
            DeleteUploadedArchiveOrMarkPending(archivePath);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException
            or UnauthorizedAccessException or InvalidDataException or JsonException
            or TaskCanceledException)
        {
            if (!consent.Token.IsCancellationRequested)
                AppToastService.ShowError(
                    "DEEP DEBUG LOG NOT SENT",
                    "The diagnostic service was unavailable. The log remains in the Deep Debug folder.");
        }
        finally
        {
            _uploadGate.Release();
        }
    }

    private bool IsCurrent(Consent consent)
    {
        lock (_choiceGate)
        {
            return CanSend() &&
                consent.Generation == _choiceGeneration &&
                !consent.Token.IsCancellationRequested;
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

    private void DeleteUploadedArchiveOrMarkPending(string archivePath)
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
            using FileStream pending = new(
                marker,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.Read,
                1,
                FileOptions.WriteThrough);
            pending.SetLength(0);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Shared storage retention provides a later bounded cleanup opportunity.
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
                if (validatedArchive is not null && TryDeleteFile(validatedArchive))
                    TryDeleteFile(markerPath);
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
        string name = Path.GetFileName(fullPath);
        return name.StartsWith("deep-debug-", StringComparison.OrdinalIgnoreCase) &&
               name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : null;
    }
}
