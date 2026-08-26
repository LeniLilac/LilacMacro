using LilacMacro.Core.Geometry;

namespace LilacMacro.App.Infrastructure;

public sealed partial class OcrRunner
{
    public Task<string> BeginWorkflowRunAsync(
        string preferredDevice,
        CancellationToken cancellationToken = default) =>
        BeginWorkflowRunAsync(preferredDevice, SmallModel, cancellationToken);

    public async Task<string> BeginWorkflowRunAsync(
        string preferredDevice,
        string modelName,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!SupportedDevices.Contains(preferredDevice))
            throw new ArgumentOutOfRangeException(nameof(preferredDevice));
        if (!SupportedModels.Contains(modelName))
            throw new ArgumentOutOfRangeException(nameof(modelName));
        _runDevices.Begin();
        _deepDebug.RecordEvent("ocr", "workflow_run_started", new
        {
            PreferredDevice = preferredDevice,
            CpuReady = IsDeviceReady(CpuDevice),
            GpuReady = IsDeviceReady(GpuDevice),
            Model = modelName,
        });
        try
        {
            if (KeepLoaded)
                await WarmUpWithRunRecoveryAsync(modelName, preferredDevice, cancellationToken)
                    .ConfigureAwait(false);
            return _runDevices.Resolve(preferredDevice);
        }
        catch
        {
            _runDevices.End();
            throw;
        }
    }

    public void EndWorkflowRun()
    {
        _runDevices.End();
        _deepDebug.RecordEvent("ocr", "workflow_run_finished");
    }

    private async Task WarmUpWithRunRecoveryAsync(
        string modelName,
        string requestedDevice,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            string actualDevice = _runDevices.Resolve(requestedDevice);
            try
            {
                _deepDebug.RecordEvent("ocr", "run_preload_started", new
                {
                    Model = modelName,
                    RequestedDevice = requestedDevice,
                    Device = actualDevice,
                });
                await _persistentWorker.WarmUpAsync(
                    modelName,
                    actualDevice,
                    cancellationToken).ConfigureAwait(false);
                if (actualDevice == GpuDevice) _runDevices.ObserveGpuSuccess();
                _deepDebug.RecordEvent("ocr", "run_preload_completed", new
                {
                    Model = modelName,
                    RequestedDevice = requestedDevice,
                    Device = actualDevice,
                });
                return;
            }
            catch (Exception error) when (TryRecoverGpuFailure(
                requestedDevice,
                actualDevice,
                modelName,
                "preload",
                error))
            {
                // The policy selected a fresh GPU retry or CPU fallback.
            }
        }
    }

    private async Task<OcrWorkerResult> RunPersistentWithRunRecoveryAsync(
        string imagePath,
        PixelRect crop,
        string cropPath,
        string modelName,
        string requestedDevice,
        CancellationToken cancellationToken,
        int scale)
    {
        while (true)
        {
            string actualDevice = _runDevices.Resolve(requestedDevice);
            try
            {
                OcrWorkerResult result = await RunPersistentWithAccessRecoveryAsync(
                    imagePath,
                    crop,
                    cropPath,
                    modelName,
                    actualDevice,
                    cancellationToken,
                    scale).ConfigureAwait(false);
                if (actualDevice == GpuDevice) _runDevices.ObserveGpuSuccess();
                return result;
            }
            catch (Exception error) when (TryRecoverGpuFailure(
                requestedDevice,
                actualDevice,
                modelName,
                "inference",
                error))
            {
                // The policy selected a fresh GPU retry or CPU fallback.
            }
        }
    }

    private bool TryRecoverGpuFailure(
        string requestedDevice,
        string actualDevice,
        string modelName,
        string stage,
        Exception error)
    {
        if (requestedDevice != GpuDevice ||
            actualDevice != GpuDevice ||
            !IsRecoverableGpuWorkerFailure(error)) return false;

        OcrGpuFailureDecision decision = _runDevices.ObserveGpuFailure(IsDeviceReady(CpuDevice));
        if (decision == OcrGpuFailureDecision.Rethrow) return false;
        _deepDebug.RecordEvent(
            "ocr",
            decision == OcrGpuFailureDecision.RetryGpu
                ? "gpu_worker_restart"
                : "gpu_fallback_to_cpu",
            new
            {
                Stage = stage,
                Model = modelName,
                RequestedDevice = requestedDevice,
                FailureType = error.GetType().Name,
                Error = error.Message,
            });
        return true;
    }

    internal static bool IsRecoverableGpuWorkerFailure(Exception error) =>
        error is not OcrWorkerApplicationControlException &&
        (error is OcrWorkerTimeoutException ||
        error is InvalidOperationException &&
        (error.Message.Contains("OCR worker stopped unexpectedly", StringComparison.OrdinalIgnoreCase) ||
         error.Message.Contains("OCR worker failed", StringComparison.OrdinalIgnoreCase)));

    private void RecordWorkerLifecycle(OcrWorkerLifecycleEvent observation) =>
        _deepDebug.RecordEvent("ocr", observation.Action, new
        {
            observation.Stage,
            observation.Device,
            observation.Model,
            observation.ElapsedMilliseconds,
        });
}
