using LilacMacro.App.Debugging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;
using LilacMacro.Runtime.Normalization;
using LilacMacro.Windows.Capture;

namespace LilacMacro.App.Runtime;

internal sealed class ExpeditionSettingsService(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private static readonly PixelRect FullClient = new(0, 0, 1366, 700);
    private static readonly PixelRect RestartDialog =
        RuntimeSearchRegionEvidenceCatalog.RestartConfirmation.Bounds;
    private readonly ExpeditionOcrService _ocr = new(workspace, ocr);
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);

    public async Task TeleportToSpawnAsync(
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        UiScalePanelMatch panel = await OpenAsync(cancellationToken).ConfigureAwait(false);
        PixelPoint point = Relative(panel.PanelBounds, 0.56, 0.65);
        await workspace.ClickRobloxAsync(DebugWorkflowCatalog.ClientSize, point, cancellationToken)
            .ConfigureAwait(false);
        status?.Invoke("TELEPORT TO SPAWN CLICKED");
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        UiScalePanelMatch current = await CapturePanelAsync(cancellationToken).ConfigureAwait(false);
        if (current.Visible)
        {
            await workspace.ClickRobloxAsync(
                DebugWorkflowCatalog.ClientSize, current.ClosePoint, cancellationToken).ConfigureAwait(false);
        }
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
    }

    public async Task RestartAsync(
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        await RestartCoreAsync(device, status, waitForPrestart: true, cancellationToken).ConfigureAwait(false);
    }

    public Task RestartForRouteRerollAsync(
        string device,
        Action<string>? status,
        CancellationToken cancellationToken) =>
        RestartCoreAsync(device, status, waitForPrestart: false, cancellationToken);

    private async Task RestartCoreAsync(
        string device,
        Action<string>? status,
        bool waitForPrestart,
        CancellationToken cancellationToken)
    {
        UiScalePanelMatch panel = await OpenAsync(cancellationToken).ConfigureAwait(false);
        PixelPoint restart = Relative(panel.PanelBounds, 0.56, 0.535);
        await workspace.ClickRobloxAsync(DebugWorkflowCatalog.ClientSize, restart, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<OcrTextRegion> dialog = await WaitForRestartDialogAsync(
            device, cancellationToken).ConfigureAwait(false);
        OcrTextRegion action = ModalActionLocator.FindPairedAction(
            dialog,
            text => Normalize(text).Contains("restart", StringComparison.Ordinal),
            text => Normalize(text).Contains("cancel", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Restart confirmation did not expose Restart.");
        if (!dialog.Any(region => Normalize(region.Text).Contains("cancel", StringComparison.Ordinal)))
            throw new InvalidOperationException("Restart confirmation did not expose Cancel.");
        await workspace.ClickRobloxAsync(
            DebugWorkflowCatalog.ClientSize, action.Bounds.Center, cancellationToken).ConfigureAwait(false);
        status?.Invoke("RESTART CONFIRMED");

        await DismissRestartDialogAsync(device, status, cancellationToken).ConfigureAwait(false);

        if (!waitForPrestart) return;

        DebugOcrSnapshot prestart = await _states.WaitForMatchAsync(
            DebugWorkflowCatalog.MatchPrestart,
            device,
            45,
            TimeSpan.FromMilliseconds(300),
            cancellationToken).ConfigureAwait(false);
        if (!prestart.Evaluation.IsMatch)
            throw new InvalidOperationException("Restart did not return to verified Expedition prestart.");
    }

    private async Task<UiScalePanelMatch> OpenAsync(CancellationToken cancellationToken)
    {
        UiScalePanelMatch alreadyOpen = await CapturePanelAsync(cancellationToken).ConfigureAwait(false);
        if (alreadyOpen.Visible && alreadyOpen.Settled) return alreadyOpen;
        RgbImage image = await CaptureRgbAsync(cancellationToken).ConfigureAwait(false);
        PixelPoint gear = UiScalePanelDetector.DetectSettingsGear(image)
            ?? throw new InvalidOperationException("Verified Settings gear was not found.");
        await workspace.ClickRobloxAsync(
            DebugWorkflowCatalog.ClientSize, gear, cancellationToken).ConfigureAwait(false);
        for (int attempt = 0; attempt < 18; attempt++)
        {
            UiScalePanelMatch panel = await CapturePanelAsync(cancellationToken).ConfigureAwait(false);
            if (panel.Visible && panel.Settled) return panel;
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException("Settings panel did not settle.");
    }

    private async Task<IReadOnlyList<OcrTextRegion>> WaitForRestartDialogAsync(
        string device,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            IReadOnlyList<OcrTextRegion> regions = await _ocr.ObserveAsync(
                RestartDialog, device, cancellationToken).ConfigureAwait(false);
            bool restart = regions.Any(region => Normalize(region.Text).Contains("restart", StringComparison.Ordinal));
            bool cancel = regions.Any(region => Normalize(region.Text).Contains("cancel", StringComparison.Ordinal));
            if (restart && cancel) return regions;
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException("Restart confirmation was not verified.");
    }

    private async Task DismissRestartDialogAsync(
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            IReadOnlyList<OcrTextRegion> regions = await _ocr.ObserveAsync(
                RestartDialog, device, cancellationToken).ConfigureAwait(false);
            if (!HasRestartConfirmation(regions))
            {
                status?.Invoke("RESTART CONFIRMATION CLOSED");
                return;
            }
            OcrTextRegion? restart = ModalActionLocator.FindPairedAction(
                regions,
                text => Normalize(text).Contains("restart", StringComparison.Ordinal),
                text => Normalize(text).Contains("cancel", StringComparison.Ordinal));
            if (restart is not null)
            {
                await workspace.ClickRobloxAsync(
                    DebugWorkflowCatalog.ClientSize, restart.Bounds.Center, cancellationToken).ConfigureAwait(false);
                status?.Invoke("RESTART CONFIRMATION RETRIED");
            }
            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException("Restart confirmation did not close.");
    }

    internal static bool HasRestartConfirmation(IReadOnlyList<OcrTextRegion> regions) =>
        regions.Any(region => Normalize(region.Text).Contains("restart", StringComparison.Ordinal)) &&
        regions.Any(region => Normalize(region.Text).Contains("cancel", StringComparison.Ordinal));

    private async Task<UiScalePanelMatch> CapturePanelAsync(CancellationToken cancellationToken) =>
        UiScalePanelDetector.DetectPanel(await CaptureRgbAsync(cancellationToken).ConfigureAwait(false));

    private async Task<RgbImage> CaptureRgbAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CapturedRgbRegion> captures = await workspace.CaptureRgbRegionsAsync(
            DebugWorkflowCatalog.ClientSize, [FullClient], cancellationToken).ConfigureAwait(false);
        return captures.Single().Image;
    }

    private static PixelPoint Relative(PixelRect panel, double x, double y) => new(
        panel.X + checked((int)Math.Round(panel.Width * x)),
        panel.Y + checked((int)Math.Round(panel.Height * y)));

    private static string Normalize(string value) => new(
        value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
