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

}
