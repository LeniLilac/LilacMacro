using LilacMacro.App.Debugging;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;
using LilacMacro.Windows.Capture;

namespace LilacMacro.Runtime.Normalization;

internal sealed record GameSettingsNormalizationResult(int Changed, int Verified);

internal sealed class GameSettingsNormalizer(
    WorkspaceController workspace,
    DeepDebugSessionService deepDebug)
{
    private static readonly PixelRect FullClient = new(0, 0, 1366, 700);
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(350);

    public async Task<GameSettingsNormalizationResult> NormalizeAsync(
        Action<string>? report,
        CancellationToken cancellationToken)
    {
        Report("NORMALIZING IN-GAME SETTINGS", report);
        UiScalePanelMatch panel = await OpenCanonicalPanelAsync(cancellationToken).ConfigureAwait(false);
        int changed = 0;
        int verified = 0;

        foreach (GameSettingsTabPlan tab in GameSettingsNormalizationPolicy.Tabs)
        {
            await SelectTabAsync(tab, cancellationToken).ConfigureAwait(false);
            (int tabChanged, int tabVerified) = await ApplyTargetsAsync(
                tab.Name, tab.InitialTargets, cancellationToken).ConfigureAwait(false);
            changed += tabChanged;
            verified += tabVerified;

            if (tab.ScrollDelta != 0 && tab.ScrolledTargets is { Count: > 0 } scrolledTargets)
            {
                await workspace.ScrollRobloxAsync(
                    DebugWorkflowCatalog.ClientSize,
                    GameSettingsNormalizationPolicy.ScrollAnchor,
                    tab.ScrollDelta,
                    TimeSpan.FromMilliseconds(600),
                    cancellationToken).ConfigureAwait(false);
                await Task.Delay(SettleDelay, cancellationToken).ConfigureAwait(false);
                await SelectScrolledTabAsync(tab, cancellationToken).ConfigureAwait(false);
                (int scrollChanged, int scrollVerified) = await ApplyTargetsAsync(
                    tab.Name, scrolledTargets, cancellationToken).ConfigureAwait(false);
                changed += scrollChanged;
                verified += scrollVerified;
            }

            Report($"{tab.Name.ToUpperInvariant()} VERIFIED", report);
        }

        await CloseCanonicalPanelAsync(cancellationToken).ConfigureAwait(false);
        Report($"IN-GAME SETTINGS NORMALIZED | {changed} CHANGED | {verified} VERIFIED", report);
        return new GameSettingsNormalizationResult(changed, verified);
    }

    private async Task<(int Changed, int Verified)> ApplyTargetsAsync(
        string tab,
        IReadOnlyList<GameSettingsToggleTarget> targets,
        CancellationToken cancellationToken)
    {
        int changed = 0;
        foreach (GameSettingsToggleTarget target in targets)
        {
            GameSettingsToggleState desired = target.DesiredOn
                ? GameSettingsToggleState.On
                : GameSettingsToggleState.Off;
            int actions = 0;
            bool targetChanged = false;
            for (int observation = 0; observation < 12; observation++)
            {
                RgbImage current = await CaptureRgbAsync(cancellationToken).ConfigureAwait(false);
                GameSettingsToggleState state = GameSettingsNormalizationPolicy.Classify(current, target.Point);
                if (state == desired)
                {
                    if (targetChanged) changed++;
                    Record("option_verified", tab, target, state, targetChanged);
                    goto Verified;
                }

                if (state != GameSettingsToggleState.Unknown && actions < 4)
                {
                    await workspace.ClickRobloxAsync(
                        DebugWorkflowCatalog.ClientSize, target.Point, cancellationToken).ConfigureAwait(false);
                    actions++;
                    targetChanged = true;
                }
                await Task.Delay(ObservationDelay(observation), cancellationToken).ConfigureAwait(false);
            }
            throw new InvalidOperationException($"{tab} option '{target.Name}' did not reach the required state after {actions} bounded attempts.");
        Verified:;
        }
        return (changed, targets.Count);
    }

    private async Task SelectTabAsync(GameSettingsTabPlan tab, CancellationToken cancellationToken)
    {
        int actions = 0;
        for (int observation = 0; observation < 12; observation++)
        {
            RgbImage image = await CaptureRgbAsync(cancellationToken).ConfigureAwait(false);
            UiScalePanelMatch panel = UiScalePanelDetector.DetectPanel(image);
            bool canonical = panel.Visible && panel.Settled &&
                UiScalePanelDetector.IsCanonicalRenderedScale(panel.RenderedScale);
            if (canonical && SelectedTabDetector.IsSelected(image, tab.TabPoint) &&
                (tab.Name != "Units" || UnitsScrollDetector.IsExpected(image, scrolled: false))) return;

            if (canonical && actions < 4)
            {
                await workspace.ClickRobloxAsync(
                    DebugWorkflowCatalog.ClientSize, tab.TabPoint, cancellationToken).ConfigureAwait(false);
                actions++;
            }
            await Task.Delay(ObservationDelay(observation), cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException($"{tab.Name} did not become the selected Settings section after {actions} bounded attempts.");
    }

    private async Task SelectScrolledTabAsync(GameSettingsTabPlan tab, CancellationToken cancellationToken)
    {
        int actions = 1;
        for (int observation = 0; observation < 12; observation++)
        {
            RgbImage image = await CaptureRgbAsync(cancellationToken).ConfigureAwait(false);
            UiScalePanelMatch panel = UiScalePanelDetector.DetectPanel(image);
            bool canonical = panel.Visible && panel.Settled &&
                UiScalePanelDetector.IsCanonicalRenderedScale(panel.RenderedScale) &&
                SelectedTabDetector.IsSelected(image, tab.TabPoint);
            if (canonical && UnitsScrollDetector.IsExpected(image, scrolled: true)) return;

            if (canonical && UnitsScrollDetector.IsExpected(image, scrolled: false) && actions < 4)
            {
                await workspace.ScrollRobloxAsync(
                    DebugWorkflowCatalog.ClientSize,
                    GameSettingsNormalizationPolicy.ScrollAnchor,
                    tab.ScrollDelta,
                    TimeSpan.FromMilliseconds(600),
                    cancellationToken).ConfigureAwait(false);
                actions++;
            }
            await Task.Delay(ObservationDelay(observation), cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException($"{tab.Name} did not reach its lower option layout after {actions} bounded scroll attempts.");
    }

    private async Task RequireTabAsync(
        GameSettingsTabPlan tab,
        bool scrolled,
        CancellationToken cancellationToken)
    {
        RgbImage image = await CaptureRgbAsync(cancellationToken).ConfigureAwait(false);
        UiScalePanelMatch panel = UiScalePanelDetector.DetectPanel(image);
        if (!panel.Visible || !panel.Settled || !UiScalePanelDetector.IsCanonicalRenderedScale(panel.RenderedScale))
            throw new InvalidOperationException($"{tab.Name} did not remain in a canonical Settings panel.");
        if (!SelectedTabDetector.IsSelected(image, tab.TabPoint))
            throw new InvalidOperationException($"{tab.Name} was not freshly verified as the selected Settings section.");
        if (tab.Name == "Units" && !UnitsScrollDetector.IsExpected(image, scrolled))
            throw new InvalidOperationException(scrolled
                ? "Units did not reach the required lower option layout."
                : "Units did not start at the required top option layout.");
    }

    private async Task<UiScalePanelMatch> OpenCanonicalPanelAsync(CancellationToken cancellationToken)
    {
        int actions = 0;
        for (int observation = 0; observation < 18; observation++)
        {
            RgbImage image = await CaptureRgbAsync(cancellationToken).ConfigureAwait(false);
            UiScalePanelMatch panel = UiScalePanelDetector.DetectPanel(image);
            if (panel.Visible && panel.Settled && UiScalePanelDetector.IsCanonicalRenderedScale(panel.RenderedScale))
                return panel;

            PixelPoint? gear = panel.Visible ? null : UiScalePanelDetector.DetectSettingsGear(image);
            if (gear is PixelPoint point && actions < 4)
            {
                await workspace.ClickRobloxAsync(
                    DebugWorkflowCatalog.ClientSize, point, cancellationToken).ConfigureAwait(false);
                actions++;
            }
            await Task.Delay(ObservationDelay(observation), cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException($"Settings did not reopen at the canonical rendered scale after {actions} bounded attempts.");
    }

    private async Task CloseCanonicalPanelAsync(CancellationToken cancellationToken)
    {
        int actions = 0;
        int destination = 0;
        for (int observation = 0; observation < 18; observation++)
        {
            RgbImage image = await CaptureRgbAsync(cancellationToken).ConfigureAwait(false);
            UiScalePanelMatch panel = UiScalePanelDetector.DetectPanel(image);
            destination = !panel.Visible && UiScalePanelDetector.DetectSettingsGear(image) is not null
                ? destination + 1
                : 0;
            if (destination >= 2) return;
            if (panel.Visible && panel.Settled &&
                UiScalePanelDetector.IsCanonicalRenderedScale(panel.RenderedScale) && actions < 4)
            {
                await workspace.ClickRobloxAsync(
                    DebugWorkflowCatalog.ClientSize, panel.ClosePoint, cancellationToken).ConfigureAwait(false);
                actions++;
            }
            await Task.Delay(ObservationDelay(observation), cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException($"Settings did not close after option normalization and {actions} bounded attempts.");
    }

    private static TimeSpan ObservationDelay(int observation) => TimeSpan.FromMilliseconds(
        Math.Min(1600, 400 * (1 << Math.Min(2, observation / 2))));

    private async Task<UiScalePanelMatch> CapturePanelAsync(CancellationToken cancellationToken) =>
        UiScalePanelDetector.DetectPanel(await CaptureRgbAsync(cancellationToken).ConfigureAwait(false));

    private async Task<RgbImage> CaptureRgbAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CapturedRgbRegion> captures = await workspace.CaptureRgbRegionsAsync(
            DebugWorkflowCatalog.ClientSize, [FullClient], cancellationToken).ConfigureAwait(false);
        return captures.Single().Image;
    }

    private void Report(string message, Action<string>? report)
    {
        report?.Invoke(message);
        deepDebug.RecordEvent("game_settings", "progress", new { Message = message });
    }

    private void Record(
        string action,
        string tab,
        GameSettingsToggleTarget target,
        GameSettingsToggleState state,
        bool changed) => deepDebug.RecordEvent(
            "game_settings",
            action,
            new
            {
                Tab = tab,
                target.Name,
                target.Point,
                Desired = target.DesiredOn ? "on" : "off",
                Observed = state.ToString().ToLowerInvariant(),
                Changed = changed,
            });

    private static class SelectedTabDetector
    {
        public static bool IsSelected(RgbImage image, PixelPoint center)
        {
            int cyan = 0;
            int samples = 0;
            for (int y = center.Y - 18; y <= center.Y + 18; y += 2)
                for (int x = center.X - 76; x <= center.X + 76; x += 2)
                {
                    int pixel = checked((y * image.Size.Width + x) * 3);
                    byte r = image.Pixels[pixel];
                    byte g = image.Pixels[pixel + 1];
                    byte b = image.Pixels[pixel + 2];
                    samples++;
                    if (g >= 85 && b >= 90 && g >= r * 1.15 && b >= r * 1.3) cyan++;
                }
            return cyan / (double)samples >= 0.45;
        }
    }

    private static class UnitsScrollDetector
    {
        public static bool IsExpected(RgbImage image, bool scrolled)
        {
            int top = 0;
            int bottom = 0;
            for (int y = 120; y <= 600; y++)
            {
                int pixel = checked((y * image.Size.Width + 1118) * 3);
                byte r = image.Pixels[pixel];
                byte g = image.Pixels[pixel + 1];
                byte b = image.Pixels[pixel + 2];
                if (b < 80 || g < 60 || b <= r * 1.3) continue;
                top = top == 0 ? y : Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
            return top != 0 && (scrolled ? top >= 260 && bottom >= 575 : top <= 170 && bottom <= 475);
        }
    }
}
