using LilacMacro.App.Debugging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;
using LilacMacro.Windows.Capture;

namespace LilacMacro.App.Runtime;

internal sealed class MatchWaveService(WorkspaceController workspace, OcrRunner ocr)
{
    private static readonly PixelRect CounterSearch = RuntimeSearchRegionEvidenceCatalog.MatchWaveCounter.Bounds;
    private static readonly TimeSpan MaximumWait = TimeSpan.FromHours(6);

    public async Task WaitForTargetAsync(
        int target,
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        if (target is < 1 or > MatchWavePolicy.MaximumTarget)
            throw new ArgumentOutOfRangeException(nameof(target));
        string directory = Path.Combine(Path.GetTempPath(), "LilacMacro", $"wave-{Guid.NewGuid():N}");
        string imagePath = Path.Combine(directory, "counter.png");
        Directory.CreateDirectory(directory);
        try
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow + MaximumWait;
            int? priorAtOrAbove = null;
            int? lastReported = null;
            while (DateTimeOffset.UtcNow < deadline)
            {
                MatchWaveObservation? observation = await ObserveAsync(
                    device,
                    imagePath,
                    cancellationToken).ConfigureAwait(false);
                if (observation is not null)
                {
                    if (lastReported != observation.Wave)
                    {
                        status?.Invoke($"WAVE {observation.Wave} VERIFIED | TARGET {target}");
                        lastReported = observation.Wave;
                    }
                    if (priorAtOrAbove is int prior &&
                        MatchWavePolicy.HasReachedTarget(target, prior, observation.Wave))
                    {
                        status?.Invoke($"INFINITE WAVE {observation.Wave} CONFIRMED TWICE");
                        return;
                    }
                    priorAtOrAbove = observation.Wave >= target ? observation.Wave : null;
                }
                else
                {
                    priorAtOrAbove = null;
                }
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
            throw new TimeoutException($"Infinite did not expose wave {target} within six hours.");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task<MatchWaveObservation?> ObserveAsync(
        string device,
        string imagePath,
        CancellationToken cancellationToken)
    {
        CapturedRgbRegion capture = (await workspace.CaptureRgbRegionsAsync(
            DebugWorkflowCatalog.ClientSize,
            [CounterSearch],
            cancellationToken).ConfigureAwait(false)).Single();
        RgbImage image = capture.Image;
        await File.WriteAllBytesAsync(imagePath, PngEncoder.Encode(image), cancellationToken).ConfigureAwait(false);
        OcrWorkerResult result = await ocr.RunAsync(
            imagePath,
            new PixelRect(0, 0, image.Size.Width, image.Size.Height),
            OcrRunner.SmallModel,
            device,
            cancellationToken).ConfigureAwait(false);
        return MatchWavePolicy.TryObserve(image, result.Regions.Select(region => new OcrTextRegion
        {
            Bounds = new PixelRect(region.X, region.Y, region.Width, region.Height),
            Text = region.Text,
            DetectionConfidence = region.DetectionConfidence,
            RecognitionConfidence = region.RecognitionConfidence,
        }).ToArray());
    }
}
