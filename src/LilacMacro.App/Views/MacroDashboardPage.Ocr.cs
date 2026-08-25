using LilacMacro.App.Notifications;
using LilacMacro.App.Infrastructure;

namespace LilacMacro.App.Views;

public partial class MacroDashboardPage
{
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private Task<bool>? _ocrSetupTask;
    private bool _ocrReady;
    private bool _ocrSetupFailed;
    private bool _ocrSetupInProgress;

    internal OcrRunner Ocr => _ocr;

    private void OwnerState_OnOcrModeChanged(object? sender, EventArgs eventArgs)
    {
        RefreshOcrReadyState();
        _ocrSetupFailed = !_ocrReady;
        UpdateStartButtonState();
    }

    private void RefreshOcrReadyState() => _ocrReady = OcrRunner.SelectDevice(
        _ownerState.OcrMode,
        _ocr.IsDeviceReady(OcrRunner.GpuDevice),
        _ocr.IsDeviceReady(OcrRunner.CpuDevice)) is not null;

    internal Task<bool> EnsureOcrReadyAsync() =>
        _ocrReady
            ? Task.FromResult(true)
            : _ocrSetupTask ??= EnsureOcrReadyCoreAsync();

    private async Task<bool> EnsureOcrReadyCoreAsync()
    {
        _ocrSetupInProgress = true;
        _ocrSetupFailed = false;
        UpdateStartButtonState();
        AppendLog("OCR SETUP | Checking local runtime");
        try
        {
            string device = await _ocr.EnsureReadyAsync(
                _ownerState.OcrMode,
                _lifecycleCancellation.Token);
            _ocrReady = true;
            AppendLog($"OCR READY | {device.ToUpperInvariant()}");
            return true;
        }
        catch (OperationCanceledException) when (_lifecycleCancellation.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception error)
        {
            _ocrSetupFailed = true;
            AppendLog($"OCR SETUP FAILED | {error.Message}");
            AppToastService.ShowError("OCR SETUP FAILED", error.Message);
            return false;
        }
        finally
        {
            _ocrSetupInProgress = false;
            _ocrSetupTask = null;
            UpdateStartButtonState();
        }
    }
}
