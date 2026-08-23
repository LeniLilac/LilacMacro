using LilacMacro.App.Infrastructure;
using LilacMacro.Core.LocalSession;

namespace LilacMacro.Runtime.Runner;

public sealed partial class HeadlessSessionRuntime
{
    private static async Task EnsureOcrAsync(
        OcrRunner ocr,
        RunnerRuntimeSnapshot snapshot,
        IProgress<SessionRuntimeProgress> progress,
        CancellationToken cancellationToken)
    {
        if (ocr.IsInstalled) return;
        progress.Report(new SessionRuntimeProgress
        {
            Stage = "ocr-setup",
            Detail = "Installing the runner OCR runtime.",
        });
        await ocr.SetupAsync(
            snapshot.PreferGpu ? OcrRunner.GpuDevice : OcrRunner.CpuDevice,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> PrepareOcrRunAsync(
        OcrRunner ocr,
        string device,
        string modelName,
        IProgress<SessionRuntimeProgress> progress,
        CancellationToken cancellationToken)
    {
        string ready = await ocr.BeginWorkflowRunAsync(
            device, modelName, cancellationToken).ConfigureAwait(false);
        progress.Report(new SessionRuntimeProgress
        {
            Stage = "ocr-ready",
            Detail = $"OCR RUN READY | {ready.ToUpperInvariant()}",
        });
        return ready;
    }

    private static string SelectDevice(OcrRunner ocr, bool preferGpu) =>
        preferGpu && ocr.IsDeviceReady(OcrRunner.GpuDevice)
            ? OcrRunner.GpuDevice
            : OcrRunner.CpuDevice;
}
