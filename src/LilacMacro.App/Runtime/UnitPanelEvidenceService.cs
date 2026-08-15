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
    private static readonly TimeSpan DismissObservationTimeout = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan UpgradeTimeout = TimeSpan.FromMinutes(3);
    private const int DismissAttempts = 8;
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);
    private PanelReference? _reference;

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
            bool physical = dps is not null && UnitPanelLayout.IsPhysicalDps(dps);
            bool phantom = dps is not null && UnitPanelLayout.IsPhantomDps(dps);
            if (layout is not null && (physical || phantom) &&
                tracker.Observe(layout) is { } stable)
            {
                IReadOnlyList<CapturedRgbRegion> reference = await workspace.CaptureRgbRegionsAsync(
                    DebugWorkflowCatalog.ClientSize,
                    [stable.PriorityControl, stable.SellControl],
                    cancellationToken);
                _reference = new PanelReference(reference[0].Image, reference[1].Image);
                status?.Invoke($"UNIT PANEL CALIBRATED {stable.UpgradeControl} {(phantom ? "PHANTOM" : "PHYSICAL")}");
                return stable;
            }
            status?.Invoke($"UNIT PANEL CALIBRATION {observation}/8");
            await Task.Delay(100, cancellationToken);
        }
        throw new InvalidOperationException("Priority, Sell, and configurable DPS evidence did not stabilize.");
    }

    public async Task<bool> WaitForConfigurableSelectionAsync(
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
            stable = observation.Physical || observation.Phantom ? stable + 1 : 0;
            if (stable >= 2)
            {
                status?.Invoke(observation.Phantom
                    ? "PHANTOM UNIT PANEL VERIFIED"
                    : "PHYSICAL UNIT PANEL VERIFIED");
                return true;
            }
            await Task.Delay(100, cancellationToken);
        }
        status?.Invoke("CONFIGURABLE UNIT PROOF TIMEOUT");
        return false;
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
        CancellationToken cancellationToken) =>
        await WaitForPanelHiddenAsync(layout, PanelTimeout, cancellationToken);

    public async Task DismissAsync(
        UnitPanelLayout layout,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        if (await WaitForPanelHiddenAsync(layout, DismissObservationTimeout, cancellationToken)) return;
        PixelPoint action = UnitPanelDismissalPolicy.ActionPoint(DebugWorkflowCatalog.ClientSize);
        for (int attempt = 1; attempt <= DismissAttempts; attempt++)
        {
            status?.Invoke($"CLOSING UNIT PANEL {attempt}/{DismissAttempts}");
            await workspace.ClickRobloxAsync(
                DebugWorkflowCatalog.ClientSize, action, cancellationToken);
            if (await WaitForPanelHiddenAsync(layout, DismissObservationTimeout, cancellationToken)) return;
        }
        throw new InvalidOperationException(
            $"The selected-unit panel remained open after {DismissAttempts} safe-corner clicks.");
    }

    private async Task<bool> WaitForPanelHiddenAsync(
        UnitPanelLayout layout,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        int hidden = 0;
        while (DateTimeOffset.UtcNow <= deadline || hidden > 0)
        {
            IReadOnlyList<CapturedRgbRegion> captures = await workspace.CaptureRgbRegionsAsync(
                DebugWorkflowCatalog.ClientSize,
                [layout.PriorityControl, layout.SellControl],
                cancellationToken);
            PanelReference reference = _reference
                ?? throw new InvalidOperationException("Selected-unit panel image reference was not calibrated.");
            bool visible = UnitPanelColorClassifier.MatchSelectedPanel(
                reference.Priority,
                reference.Sell,
                captures[0].Image,
                captures[1].Image).IsMatch;
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
        PanelReference reference = _reference
            ?? throw new InvalidOperationException("Selected-unit panel image reference was not calibrated.");
        if (!UnitPanelColorClassifier.MatchSelectedPanel(
                reference.Priority,
                reference.Sell,
                captures[0].Image,
                captures[1].Image).IsMatch)
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

    private sealed record PanelReference(RgbImage Priority, RgbImage Sell);
}
