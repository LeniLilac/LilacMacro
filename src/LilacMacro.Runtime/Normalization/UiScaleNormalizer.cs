using LilacMacro.App.Debugging;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;
using LilacMacro.Windows.Capture;

namespace LilacMacro.Runtime.Normalization;

internal sealed record UiScaleNormalizationResult(bool Applied, double RenderedScale);

internal sealed class UiScaleNormalizer(
    WorkspaceController workspace,
    OcrRunner ocr,
    DeepDebugSessionService deepDebug)
{
    private const int MaximumFeedbackAttempts = 5;
    private static readonly PixelRect FullClient = new(0, 0, 1366, 700);
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(350);
    private readonly DebugOcrController _debug = new(workspace, ocr);
    private readonly UiScaleCalibrationStore _calibration = new();

    public async Task<UiScaleNormalizationResult> NormalizeAsync(
        string device,
        Action<string>? report,
        CancellationToken cancellationToken)
    {
        if (!ocr.IsDeviceReady(device))
            throw new InvalidOperationException($"OCR {device.ToUpperInvariant()} is not set up.");

        Report("NORMALIZING UI SCALE", report);
        await _debug.PrepareAsync(cancellationToken).ConfigureAwait(false);
        await WaitForLobbyAsync(device, "before opening Settings", cancellationToken).ConfigureAwait(false);

        PixelPoint gear = await DetectGearAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The verified Lobby did not expose the fixed Settings gear.");
        Record("gear_verified", new { Point = gear });
        await workspace.ClickRobloxAsync(DebugWorkflowCatalog.ClientSize, gear, cancellationToken).ConfigureAwait(false);

        UiScalePanelMatch panel = await WaitForPanelAsync(requireCanonical: false, cancellationToken).ConfigureAwait(false);
        SettingsSearchEvidence search = await ObserveSettingsSearchAsync(panel, device, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Settings opened, but fresh OCR could not verify its search field and navigation rail.");
        Record("settings_verified", new { panel.RenderedScale, search.Evidence, search.SearchPoint });

        await workspace.ClickRobloxAsync(
            DebugWorkflowCatalog.ClientSize,
            search.SearchPoint,
            cancellationToken).ConfigureAwait(false);
        await workspace.RunKeySequenceAsync(
            DebugWorkflowCatalog.ClientSize,
            SearchSequence(),
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(700), cancellationToken).ConfigureAwait(false);

        double? cachedCandidate = await _calibration.LoadAsync(cancellationToken).ConfigureAwait(false);
        double candidate = cachedCandidate ?? UiScaleFeedbackPolicy.TargetRenderedScale;
        if (cachedCandidate is not null)
        {
            Report($"TRYING CACHED UI SCALE {UiScaleFeedbackPolicy.Format(candidate)}", report);
            Record("ui_scale_cache_hit", new { Candidate = candidate });
        }
        for (int attempt = 1; attempt <= MaximumFeedbackAttempts; attempt++)
        {
            panel = await WaitForPanelAsync(requireCanonical: false, cancellationToken).ConfigureAwait(false);
            UiScaleRowEvidence row = await ObserveScaleRowAsync(panel, device, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Settings search did not expose a verified UI Scale row.");
            Record("ui_scale_row_verified", new
            {
                Attempt = attempt,
                Candidate = candidate,
                row.ValuePoint,
                row.Evidence,
                panel.RenderedScale,
            });

            Report($"CALIBRATING UI SCALE {UiScaleFeedbackPolicy.Format(candidate)}", report);
            await workspace.ClickRobloxAsync(
                DebugWorkflowCatalog.ClientSize,
                row.ValuePoint,
                cancellationToken).ConfigureAwait(false);
            await workspace.RunKeySequenceAsync(
                DebugWorkflowCatalog.ClientSize,
                ScaleSequence(candidate),
                cancellationToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(700), cancellationToken).ConfigureAwait(false);

            panel = await WaitForPanelAsync(requireCanonical: false, cancellationToken).ConfigureAwait(false);
            Report(
                $"UI SCALE INPUT {UiScaleFeedbackPolicy.Format(candidate)} | RENDERED {panel.RenderedScale:0.000}",
                report);
            Record("ui_scale_feedback", new
            {
                Attempt = attempt,
                Candidate = candidate,
                ObservedRenderedScale = panel.RenderedScale,
            });
            if (UiScalePanelDetector.IsCanonicalRenderedScale(panel.RenderedScale)) break;

            if (attempt == 1 && cachedCandidate is not null)
            {
                Record("ui_scale_cache_stale", new
                {
                    Candidate = candidate,
                    ObservedRenderedScale = panel.RenderedScale,
                });
            }

            double corrected = UiScaleFeedbackPolicy.Correct(candidate, panel.RenderedScale);
            if (corrected == candidate)
            {
                throw new InvalidOperationException(
                    $"The canonical rendered UI size cannot be reached on this device. Input {candidate:0.00} renders as {panel.RenderedScale:0.000}, and the supported input range is {UiScaleFeedbackPolicy.MinimumValue:0.00} to {UiScaleFeedbackPolicy.MaximumValue:0.00}.");
            }
            if (attempt == MaximumFeedbackAttempts)
            {
                throw new InvalidOperationException(
                    $"UI Scale did not converge after {MaximumFeedbackAttempts} bounded feedback adjustments. The last input {candidate:0.00} rendered as {panel.RenderedScale:0.000}.");
            }
            candidate = corrected;
        }

        UiScaleRowEvidence verified = await ObserveScaleRowAsync(panel, device, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("UI Scale changed, but the semantic UI Scale row could not be reverified.");
        UiScalePanelMatch closeEvidence = await CapturePanelAsync(cancellationToken).ConfigureAwait(false);
        if (!closeEvidence.Visible || !closeEvidence.Settled ||
            !UiScalePanelDetector.IsCanonicalRenderedScale(closeEvidence.RenderedScale))
            throw new InvalidOperationException("Settings did not remain stable at the canonical rendered scale before closing.");
        Record("ui_scale_verified", new
        {
            verified.Evidence,
            closeEvidence.RenderedScale,
            closeEvidence.ClosePoint,
            Applied = true,
        });
        await workspace.ClickRobloxAsync(
            DebugWorkflowCatalog.ClientSize,
            closeEvidence.ClosePoint,
            cancellationToken).ConfigureAwait(false);
        await WaitForLobbyAsync(device, "after closing Settings", cancellationToken).ConfigureAwait(false);
        await SaveCalibrationBestEffortAsync(candidate, cancellationToken).ConfigureAwait(false);
        Report("UI SCALE NORMALIZED | LOBBY VERIFIED", report);
        return new UiScaleNormalizationResult(true, closeEvidence.RenderedScale);
    }

    private async Task SaveCalibrationBestEffortAsync(double candidate, CancellationToken cancellationToken)
    {
        try
        {
            await _calibration.SaveAsync(candidate, cancellationToken).ConfigureAwait(false);
            Record("ui_scale_cache_saved", new { Candidate = candidate });
        }
        catch (IOException exception)
        {
            Record("ui_scale_cache_save_failed", new { exception.Message });
        }
        catch (UnauthorizedAccessException exception)
        {
            Record("ui_scale_cache_save_failed", new { exception.Message });
        }
    }

    private async Task<PixelPoint?> DetectGearAsync(CancellationToken cancellationToken)
    {
        RgbImage image = await CaptureRgbAsync(cancellationToken).ConfigureAwait(false);
        return UiScalePanelDetector.DetectSettingsGear(image);
    }

    private async Task<UiScalePanelMatch> CapturePanelAsync(CancellationToken cancellationToken)
    {
        RgbImage image = await CaptureRgbAsync(cancellationToken).ConfigureAwait(false);
        return UiScalePanelDetector.DetectPanel(image);
    }

    private async Task<RgbImage> CaptureRgbAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CapturedRgbRegion> captures = await workspace.CaptureRgbRegionsAsync(
            DebugWorkflowCatalog.ClientSize,
            [FullClient],
            cancellationToken).ConfigureAwait(false);
        return captures.Single().Image;
    }

    private async Task<UiScalePanelMatch> WaitForPanelAsync(
        bool requireCanonical,
        CancellationToken cancellationToken)
    {
        UiScalePanelMatch previous = default;
        int stable = 0;
        for (int observation = 1; observation <= 18; observation++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UiScalePanelMatch current = await CapturePanelAsync(cancellationToken).ConfigureAwait(false);
            bool acceptable = current.Visible && current.Settled &&
                (!requireCanonical || UiScalePanelDetector.IsCanonicalRenderedScale(current.RenderedScale));
            stable = acceptable && previous.Visible &&
                     Math.Abs(current.RenderedScale - previous.RenderedScale) <= 0.006
                ? stable + 1
                : acceptable ? 1 : 0;
            Record("panel_observed", new
            {
                Observation = observation,
                current.Visible,
                current.Settled,
                current.RenderedScale,
                current.Confidence,
                Stable = stable,
                RequireCanonical = requireCanonical,
            });
            if (stable >= 2) return current;
            previous = current;
            await Task.Delay(PollDelay, cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException(requireCanonical
            ? "Settings did not settle at the canonical 1.00 rendered scale."
            : "Settings did not open as a stable, structurally verified panel.");
    }

    private async Task<SettingsSearchEvidence?> ObserveSettingsSearchAsync(
        UiScalePanelMatch panel,
        string device,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OcrTextRegion> regions = await ObserveOcrAsync(panel.PanelBounds, device, cancellationToken)
            .ConfigureAwait(false);
        return UiScaleOcrPolicy.FindSettingsSearch(regions, panel);
    }

    private async Task<UiScaleRowEvidence?> ObserveScaleRowAsync(
        UiScalePanelMatch panel,
        string device,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OcrTextRegion> regions = await ObserveOcrAsync(panel.PanelBounds, device, cancellationToken)
            .ConfigureAwait(false);
        return UiScaleOcrPolicy.FindUiScaleRow(regions, panel);
    }

    private async Task<IReadOnlyList<OcrTextRegion>> ObserveOcrAsync(
        PixelRect region,
        string device,
        CancellationToken cancellationToken)
    {
        CapturedPng capture = await workspace.CaptureLiveFrameAsync(
            DebugWorkflowCatalog.ClientSize,
            cancellationToken).ConfigureAwait(false);
        string temporaryRoot = Path.Combine(Path.GetTempPath(), "LilacMacro", $"ui-scale-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        string imagePath = Path.Combine(temporaryRoot, "frame.png");
        try
        {
            await File.WriteAllBytesAsync(imagePath, capture.Bytes, cancellationToken).ConfigureAwait(false);
            OcrWorkerResult result = await ocr.RunAsync(
                imagePath,
                region,
                OcrRunner.SmallModel,
                device,
                cancellationToken).ConfigureAwait(false);
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
            TryDelete(temporaryRoot);
        }
    }

    private async Task WaitForLobbyAsync(
        string device,
        string phase,
        CancellationToken cancellationToken)
    {
        int consecutive = 0;
        for (int observation = 1; observation <= 12; observation++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DebugRunReport report = await _debug.CheckLobbyAsync(device, cancellationToken).ConfigureAwait(false);
            consecutive = report.Succeeded ? consecutive + 1 : 0;
            Record("lobby_observed", new { Phase = phase, Observation = observation, report.Succeeded, report.Status, Consecutive = consecutive });
            if (consecutive >= 2) return;
            await Task.Delay(PollDelay, cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException($"Fresh Lobby evidence was not stable {phase}.");
    }

    private static AutomationKeySequence SearchSequence() => AutomationKeySequence.Create(
        [
            .. Enumerable.Repeat(new AutomationKeyPress(0x08, 30), 20),
            new(0x55, 30), new(0x49, 30), new(0x20, 30), new(0x53, 30),
            new(0x43, 30), new(0x41, 30), new(0x4C, 30), new(0x45, 30), new(0x0D, 50),
        ]);

    private static AutomationKeySequence ScaleSequence(double value)
    {
        IEnumerable<AutomationKeyPress> valueKeys = UiScaleFeedbackPolicy.Format(value)
            .Select(character => new AutomationKeyPress(ScaleVirtualKey(character), 30));
        return AutomationKeySequence.Create(
            [
                .. Enumerable.Repeat(new AutomationKeyPress(0x08, 30), 8),
                .. valueKeys,
                new(0x0D, 50),
            ]);
    }

    private static int ScaleVirtualKey(char character) => character switch
    {
        >= '0' and <= '9' => character,
        '.' => 0xBE,
        _ => throw new InvalidDataException($"Unsupported UI Scale input character '{character}'."),
    };

    private void Report(string message, Action<string>? report)
    {
        report?.Invoke(message);
        Record("progress", new { Message = message });
    }

    private void Record(string name, object data) => deepDebug.RecordEvent("ui_scale", name, data);

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
