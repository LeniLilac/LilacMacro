using LilacMacro.App.Debugging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;
using LilacMacro.Core.Placements;
using LilacMacro.Windows.Capture;

namespace LilacMacro.App.Runtime;

internal sealed class UnitPanelEvidenceService(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private static readonly TimeSpan PanelTimeout = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan UpgradeTimeout = TimeSpan.FromMinutes(3);
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);

    public async Task<UnitPanelLayout> CalibrateAsync(
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        UnitPanelLayoutTracker tracker = new();
        for (int observation = 1; observation <= 8; observation++)
        {
            DebugOcrSnapshot snapshot = await _states.RunAsync(
                DebugWorkflowCatalog.UnitPanel, device, cancellationToken);
            UnitPanelLayout? layout = UnitPanelLayout.TryCreate(snapshot.Regions, DebugWorkflowCatalog.ClientSize);
            string? dps = layout is null
                ? null
                : snapshot.Regions.FirstOrDefault(region => region.Bounds == layout.DpsText)?.Text;
            if (dps is not null && UnitPanelLayout.IsPhantomDps(dps))
                throw new InvalidOperationException("Phantom placement detected from DPS ???.");
            if (layout is not null && dps is not null && UnitPanelLayout.IsPhysicalDps(dps) &&
                tracker.Observe(layout) is { } stable)
            {
                status?.Invoke($"UNIT PANEL CALIBRATED {stable.UpgradeControl}");
                return stable;
            }
            status?.Invoke($"UNIT PANEL CALIBRATION {observation}/8");
            await Task.Delay(100, cancellationToken);
        }
        throw new InvalidOperationException("Priority, Sell, and physical DPS evidence did not stabilize.");
    }

    public async Task<bool> WaitForPhysicalSelectionAsync(
        UnitPanelLayout layout,
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + PanelTimeout;
        int stable = 0;
        while (DateTimeOffset.UtcNow <= deadline || stable > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PanelObservation observation = await ObservePanelAsync(layout, device, cancellationToken);
            if (observation.Phantom)
            {
                status?.Invoke("PHANTOM PLACEMENT REJECTED: DPS ???");
                return false;
            }
            stable = observation.Physical ? stable + 1 : 0;
            if (stable >= 2) return true;
            await Task.Delay(100, cancellationToken);
        }
        status?.Invoke("SELECTED UNIT PROOF TIMEOUT");
        return false;
    }

    public async Task<UnitUpgradeState> WaitForUpgradeAsync(
        UnitPanelLayout layout,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + UpgradeTimeout;
        StableUnitUpgradeTracker tracker = new();
        bool reportedWait = false;
        while (DateTimeOffset.UtcNow <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<CapturedRgbRegion> captures = await workspace.CaptureRgbRegionsAsync(
                DebugWorkflowCatalog.ClientSize,
                [layout.UpgradeMain, layout.UpgradeExtension],
                cancellationToken);
            UnitUpgradeObservation observation = UnitPanelColorClassifier.ClassifyUpgrade(
                captures[0].Image, captures[1].Image);
            UnitUpgradeState stable = tracker.Observe(observation.State);
            if (stable is UnitUpgradeState.Affordable or UnitUpgradeState.Maxed) return stable;
            if (observation.State == UnitUpgradeState.Unaffordable && !reportedWait)
            {
                status?.Invoke("UPGRADE UNAFFORDABLE; WAITING");
                reportedWait = true;
            }
            if (observation.State == UnitUpgradeState.Unknown)
                throw new InvalidOperationException("Upgrade control evidence became ambiguous.");
            await Task.Delay(250, cancellationToken);
        }
        throw new TimeoutException("Upgrade did not become affordable within three minutes.");
    }

    public async Task<bool> WaitForPanelHiddenAsync(
        UnitPanelLayout layout,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + PanelTimeout;
        int hidden = 0;
        while (DateTimeOffset.UtcNow <= deadline || hidden > 0)
        {
            IReadOnlyList<CapturedRgbRegion> captures = await workspace.CaptureRgbRegionsAsync(
                DebugWorkflowCatalog.ClientSize,
                [layout.PriorityControl, layout.SellControl],
                cancellationToken);
            bool visible = UnitPanelColorClassifier.IsSelectedPanel(captures[0].Image, captures[1].Image);
            hidden = visible ? 0 : hidden + 1;
            if (hidden >= 2) return true;
            await Task.Delay(100, cancellationToken);
        }
        return false;
    }

    private async Task<PanelObservation> ObservePanelAsync(
        UnitPanelLayout layout,
        string device,
        CancellationToken cancellationToken)
    {
        PixelRect dps = Inflate(layout.DpsText, 8, DebugWorkflowCatalog.ClientSize);
        IReadOnlyList<CapturedRgbRegion> captures = await workspace.CaptureRgbRegionsAsync(
            DebugWorkflowCatalog.ClientSize,
            [layout.PriorityControl, layout.SellControl, dps],
            cancellationToken);
        if (!UnitPanelColorClassifier.IsSelectedPanel(captures[0].Image, captures[1].Image))
            return new PanelObservation(false, false);

        OcrWorkerResult result = await RunTinyOcrAsync(captures[2].Image, device, cancellationToken);
        string text = string.Join(' ', result.Regions.Select(region => region.Text).Prepend(result.Text));
        bool phantom = UnitPanelLayout.IsPhantomDps(text);
        return new PanelObservation(!phantom && UnitPanelLayout.IsPhysicalDps(text), phantom);
    }

    private async Task<OcrWorkerResult> RunTinyOcrAsync(
        RgbImage image,
        string device,
        CancellationToken cancellationToken)
    {
        string directory = Path.Combine(Path.GetTempPath(), "LilacMacro", $"unit-dps-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "dps.png");
        try
        {
            await File.WriteAllBytesAsync(path, PngEncoder.Encode(image), cancellationToken);
            return await ocr.RunAsync(
                path, new PixelRect(0, 0, image.Size.Width, image.Size.Height),
                OcrRunner.SmallModel, device, cancellationToken);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static PixelRect Inflate(PixelRect region, int amount, PixelSize bounds)
    {
        int left = Math.Max(0, region.X - amount);
        int top = Math.Max(0, region.Y - amount);
        int right = Math.Min(bounds.Width, region.Right + amount);
        int bottom = Math.Min(bounds.Height, region.Bottom + amount);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    private sealed record PanelObservation(bool Physical, bool Phantom);
}
