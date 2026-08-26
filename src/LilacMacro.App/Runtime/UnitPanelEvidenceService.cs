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
    private static readonly TimeSpan MaxedOcrGrace = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan MaxedOcrInterval = TimeSpan.FromSeconds(3);
    private const int DismissAttempts = 8;
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);
    private PanelReference? _reference;
    private UnitPanelLayout? _dpsCalibratedLayout;
    private UnitPanelDpsCapturePlan? _dpsCapturePlan;
    private UnitPanelDpsFingerprintBuilder? _dpsFingerprintBuilder;
    private UnitPanelDpsKind? _lastDpsKind;
    private RgbImage? _pendingDpsSample;
    private int _stableDpsSamples;
    private RgbImage? _maxedReference;

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
            if (layout is not null && tracker.Observe(layout) is { } stable)
            {
                _dpsCalibratedLayout = stable;
                UnitPanelDpsCapturePlan capturePlan = UnitPanelDpsCapturePlan.Create(
                    stable, DebugWorkflowCatalog.ClientSize);
                _dpsCapturePlan = capturePlan;
                _dpsFingerprintBuilder = new(
                    capturePlan,
                    new PixelSize(capturePlan.Region.Width, capturePlan.Region.Height));
                _lastDpsKind = null;
                _pendingDpsSample = null;
                _stableDpsSamples = 0;
                IReadOnlyList<CapturedRgbRegion> reference = await workspace.CaptureRgbRegionsAsync(
                    DebugWorkflowCatalog.ClientSize,
                    [
                        stable.PriorityControl,
                        stable.SellControl,
                    ],
                    cancellationToken);
                _reference = new PanelReference(reference[0].Image, reference[1].Image);
                _maxedReference = null;
                status?.Invoke($"UNIT PANEL CALIBRATED {stable.UpgradeControl} DPS ROI {capturePlan.Region}");
                return stable;
            }
            status?.Invoke($"UNIT PANEL CALIBRATION {observation}/8");
            await Task.Delay(100, cancellationToken);
        }
        throw new InvalidOperationException("Priority, Sell, and DPS panel geometry did not stabilize.");
    }

    public async Task NormalizeSelectionAsync(
        UnitPanelLayout? layout,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        await workspace.CaptureLiveFrameAsync(
            DebugWorkflowCatalog.ClientSize,
            cancellationToken,
            "placement-selection-normalization");
        status?.Invoke("NORMALIZING UNIT SELECTION");
        await workspace.ClickRobloxAsync(
            DebugWorkflowCatalog.ClientSize,
            UnitPanelDismissalPolicy.ActionPoint(DebugWorkflowCatalog.ClientSize),
            cancellationToken);
        if (layout is not null)
            await DismissAsync(layout, status, cancellationToken);
    }

    public async Task<bool> WaitForSelectedPanelAsync(
        UnitPanelLayout layout,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + PanelTimeout;
        int stable = 0;
        UnitPanelImageMatch? last = null;
        while (DateTimeOffset.UtcNow <= deadline || stable > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            last = await ObserveSelectedPanelAsync(layout, cancellationToken);
            stable = last.IsMatch ? stable + 1 : 0;
            if (stable >= 2)
            {
                status?.Invoke("SELECTED UNIT PANEL VERIFIED");
                return true;
            }
            await Task.Delay(100, cancellationToken);
        }
        status?.Invoke(last is null
            ? "SELECTED UNIT PANEL PROOF TIMEOUT; NO OBSERVATION"
            : $"SELECTED UNIT PANEL PROOF TIMEOUT; " +
              $"PRIORITY BLUE {last.PriorityBlueFraction:F3} RED {last.PriorityRedFraction:F3}; " +
              $"SELL RED {last.SellRedFraction:F3} BLUE {last.SellBlueFraction:F3}; " +
              $"SIMILARITY {last.PrioritySimilarity:F3}/{last.SellSimilarity:F3}");
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
            PanelObservation observation = await ObservePanelAsync(
                layout, device, status, cancellationToken);
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
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + UpgradeTimeout;
        StableUnitUpgradeTracker tracker = new();
        bool reportedWait = false;
        DateTimeOffset? graySince = null;
        DateTimeOffset nextMaxedOcr = DateTimeOffset.MinValue;
        while (DateTimeOffset.UtcNow <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<CapturedRgbRegion> captures = await workspace.CaptureRgbRegionsAsync(
                DebugWorkflowCatalog.ClientSize,
                [
                    layout.UpgradeFillPrimary,
                    layout.UpgradeFillSecondary,
                    layout.UpgradeMaxedReference,
                    layout.UpgradeControl,
                ],
                cancellationToken);
            UnitUpgradeObservation observation = UnitPanelColorClassifier.ClassifyUpgrade(
                captures[0].Image, captures[1].Image);
            UnitUpgradeState stable = tracker.Observe(observation.State);
            if (stable == UnitUpgradeState.Affordable) return stable;
            if (observation.State == UnitUpgradeState.Unaffordable && !reportedWait)
            {
                status?.Invoke("UPGRADE UNAFFORDABLE; WAITING");
                reportedWait = true;
            }
            if (observation.State == UnitUpgradeState.Unknown)
                throw new InvalidOperationException("Upgrade control evidence became ambiguous.");
            if (observation.State != UnitUpgradeState.Unaffordable)
            {
                graySince = null;
            }
            else
            {
                graySince ??= DateTimeOffset.UtcNow;
            }
            if (stable == UnitUpgradeState.Unaffordable &&
                _maxedReference is not null &&
                UnitPanelColorClassifier.MatchConfirmedMaxed(_maxedReference, captures[2].Image))
            {
                status?.Invoke("UNIT MAXED REFERENCE VERIFIED");
                return UnitUpgradeState.Maxed;
            }
            if (stable == UnitUpgradeState.Unaffordable &&
                graySince is { } since && DateTimeOffset.UtcNow - since >= MaxedOcrGrace &&
                DateTimeOffset.UtcNow >= nextMaxedOcr)
            {
                OcrWorkerResult result = await RunTinyOcrAsync(captures[3].Image, device, cancellationToken);
                string text = string.Join(' ', result.Regions.Select(region => region.Text).Prepend(result.Text));
                if (UnitPanelColorClassifier.IsMaxedText(text))
                {
                    _maxedReference = captures[2].Image;
                    status?.Invoke("UNIT MAXED OCR VERIFIED; REFERENCE SAVED");
                    return UnitUpgradeState.Maxed;
                }
                nextMaxedOcr = DateTimeOffset.UtcNow + MaxedOcrInterval;
            }
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
            UnitPanelImageMatch observation = await ObserveSelectedPanelAsync(layout, cancellationToken);
            hidden = observation.IsMatch ? 0 : hidden + 1;
            if (hidden >= 2) return true;
            await Task.Delay(100, cancellationToken);
        }
        return false;
    }

    private async Task<UnitPanelImageMatch> ObserveSelectedPanelAsync(
        UnitPanelLayout layout,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CapturedRgbRegion> captures = await workspace.CaptureRgbRegionsAsync(
            DebugWorkflowCatalog.ClientSize,
            [layout.PriorityControl, layout.SellControl],
            cancellationToken);
        PanelReference reference = _reference
            ?? throw new InvalidOperationException("Selected-unit panel image reference was not calibrated.");
        return UnitPanelColorClassifier.MatchSelectedPanel(
            reference.Priority,
            reference.Sell,
            captures[0].Image,
            captures[1].Image);
    }

    private async Task<PanelObservation> ObservePanelAsync(
        UnitPanelLayout layout,
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        EnsureDpsCalibration(layout);
        UnitPanelDpsCapturePlan dps = _dpsCapturePlan
            ?? throw new InvalidOperationException("DPS capture plan was not calibrated.");
        IReadOnlyList<CapturedRgbRegion> captures = await workspace.CaptureRgbRegionsAsync(
            DebugWorkflowCatalog.ClientSize,
            [layout.PriorityControl, layout.SellControl, dps.Region],
            cancellationToken);
        PanelReference reference = _reference
            ?? throw new InvalidOperationException("Selected-unit panel image reference was not calibrated.");
        if (!UnitPanelColorClassifier.MatchSelectedPanel(
                reference.Priority,
                reference.Sell,
                captures[0].Image,
                captures[1].Image).IsMatch)
            return new PanelObservation(false, false);

        if (_dpsFingerprintBuilder?.Fingerprint is { } fingerprint)
        {
            UnitPanelDpsImageMatch imageMatch = fingerprint.Match(captures[2].Image);
            if (imageMatch.IsExact)
            {
                status?.Invoke(
                    $"DPS IMAGE FAST PATH EXACT 1.00 " +
                    $"({imageMatch.MatchingPixels}/{imageMatch.ComparedPixels})");
                return new PanelObservation(false, true);
            }
            status?.Invoke($"DPS IMAGE FAST PATH MISS {imageMatch.ExactFraction:F3}; OCR FALLBACK");
        }

        OcrWorkerResult result = await RunTinyOcrAsync(captures[2].Image, device, cancellationToken);
        string text = string.Join(' ', result.Regions.Select(region => region.Text).Prepend(result.Text));
        bool phantom = UnitPanelLayout.IsPhantomDps(text);
        bool physical = !phantom && UnitPanelLayout.IsPhysicalDps(text);
        if (phantom || physical)
        {
            RecordDpsSample(
                phantom ? UnitPanelDpsKind.Phantom : UnitPanelDpsKind.Physical,
                captures[2].Image);
            status?.Invoke(phantom ? "DPS OCR FALLBACK PHANTOM" : "DPS OCR FALLBACK PHYSICAL");
        }
        return new PanelObservation(physical, phantom);
    }

    private async Task<OcrWorkerResult> RunTinyOcrAsync(
        RgbImage image,
        string device,
        CancellationToken cancellationToken)
    {
        string directory = Path.Combine(Path.GetTempPath(), "LilacMacro", $"unit-panel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "panel.png");
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

    private void EnsureDpsCalibration(UnitPanelLayout layout)
    {
        if (_dpsCapturePlan is not null &&
            _dpsCalibratedLayout is { } calibrated && calibrated.IsCloseTo(layout))
            return;

        _dpsCalibratedLayout = layout;
        UnitPanelDpsCapturePlan capturePlan = UnitPanelDpsCapturePlan.Create(
            layout, DebugWorkflowCatalog.ClientSize);
        _dpsCapturePlan = capturePlan;
        _dpsFingerprintBuilder = new(
            capturePlan,
            new PixelSize(capturePlan.Region.Width, capturePlan.Region.Height));
        _lastDpsKind = null;
        _pendingDpsSample = null;
        _stableDpsSamples = 0;
    }

    private void RecordDpsSample(UnitPanelDpsKind kind, RgbImage image)
    {
        if (_lastDpsKind != kind)
        {
            _lastDpsKind = kind;
            _stableDpsSamples = 1;
            _pendingDpsSample = image;
            return;
        }

        _stableDpsSamples++;
        if (_stableDpsSamples == 2 && _pendingDpsSample is { } pending)
        {
            _dpsFingerprintBuilder?.AddSample(kind, pending);
            _dpsFingerprintBuilder?.AddSample(kind, image);
            _pendingDpsSample = null;
            return;
        }

        if (_stableDpsSamples > 2)
            _dpsFingerprintBuilder?.AddSample(kind, image);
    }

    private sealed record PanelObservation(bool Physical, bool Phantom);

    private sealed record PanelReference(RgbImage Priority, RgbImage Sell);
}
