using LilacMacro.App.Debugging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Imaging;
using LilacMacro.Windows.Capture;

namespace LilacMacro.App.Runtime;

internal sealed class ExpeditionOcrService(WorkspaceController workspace, OcrRunner ocr)
{
    public async Task<IReadOnlyList<OcrTextRegion>> ObserveAsync(
        PixelRect region,
        string device,
        CancellationToken cancellationToken,
        int scale = 1)
    {
        CapturedPng frame = await workspace.CaptureLiveFrameAsync(
            DebugWorkflowCatalog.ClientSize, cancellationToken).ConfigureAwait(false);
        string root = Path.Combine(Path.GetTempPath(), "LilacMacro", $"expedition-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "frame.png");
        try
        {
            await File.WriteAllBytesAsync(path, frame.Bytes, cancellationToken).ConfigureAwait(false);
            OcrWorkerResult result = await ocr.RunAsync(
                path, region, OcrRunner.SmallModel, device, cancellationToken, scale).ConfigureAwait(false);
            return result.Regions.Select(candidate => new OcrTextRegion
            {
                Bounds = new PixelRect(candidate.X, candidate.Y, candidate.Width, candidate.Height),
                Text = candidate.Text,
                DetectionConfidence = candidate.DetectionConfidence,
                RecognitionConfidence = candidate.RecognitionConfidence,
            }).ToArray();
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
