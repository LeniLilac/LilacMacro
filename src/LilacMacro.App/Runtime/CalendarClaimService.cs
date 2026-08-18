using LilacMacro.App.Debugging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Runtime;

internal sealed class CalendarClaimService(
    WorkspaceController workspace,
    OcrRunner ocr,
    UtilityRespawnService respawn)
{
    private static readonly PixelSize ClientSize = DebugWorkflowCatalog.ClientSize;
    internal static readonly PixelRect LauncherSearch =
        RuntimeSearchRegionEvidenceCatalog.CalendarLauncher.Bounds;
    internal static readonly PixelRect RewardGrid =
        RuntimeSearchRegionEvidenceCatalog.CalendarRewardGrid.Bounds;
    private static readonly TimeSpan ObservationDelay = TimeSpan.FromMilliseconds(300);
    private readonly ExpeditionOcrService _observations = new(workspace, ocr);
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);

    public async Task RunAsync(
        int? areasMenuVirtualKey,
        int reservedVirtualKey,
        string device,
        Action<string> status,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(status);
        status("CALENDAR | OPENING");
        IReadOnlyList<OcrTextRegion> launcher = await RequireLauncherAsync(device, cancellationToken)
            .ConfigureAwait(false);
        OcrTargetMatch calendar = OcrRuleEngine.FindExactTarget(
            new OcrTargetRule("Calendar", "calendar"), launcher)
            ?? throw new InvalidOperationException("Verified lobby launcher did not expose Calendar.");
        await workspace.ClickRobloxAsync(ClientSize, calendar.Region.Bounds.Center, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<OcrTextRegion> open = await WaitForCalendarAsync(device, cancellationToken)
            .ConfigureAwait(false);
        for (int pass = 0; pass < CalendarClaimPolicy.Passes; pass++)
        {
            for (int index = 0; index < 7; index++)
            {
                if (!CalendarClaimPolicy.TryResolveClaimPoints(open, ClientSize, out IReadOnlyList<PixelPoint> points))
                    throw new InvalidOperationException("The live Reward Calendar grid was not safe to use.");
                status($"CALENDAR | CLAIM PASS {pass + 1}/{CalendarClaimPolicy.Passes} | DAY {7 - index}");
                await workspace.ClickRobloxAsync(ClientSize, points[index], cancellationToken).ConfigureAwait(false);
                await Task.Delay(ObservationDelay, cancellationToken).ConfigureAwait(false);
                open = await RequireCalendarAsync(device, cancellationToken).ConfigureAwait(false);
            }
        }

        status("CALENDAR | RESPAWNING");
        await respawn.RunAsync(
            areasMenuVirtualKey, reservedVirtualKey, device, cancellationToken).ConfigureAwait(false);
        status("CALENDAR | COMPLETE | LOBBY VERIFIED");
    }

    private async Task<IReadOnlyList<OcrTextRegion>> RequireLauncherAsync(
        string device,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OcrTextRegion> regions = await _observations.ObserveAsync(
            LauncherSearch,
            device,
            cancellationToken).ConfigureAwait(false);
        bool calendar = OcrRuleEngine.FindExactTarget(new OcrTargetRule("Calendar", "calendar"), regions) is not null;
        return calendar
            ? regions
            : throw new InvalidOperationException("Calendar launcher was not freshly verified in Lobby.");
    }

    private async Task<IReadOnlyList<OcrTextRegion>> WaitForCalendarAsync(
        string device,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            IReadOnlyList<OcrTextRegion> regions = await _observations.ObserveAsync(
                RewardGrid,
                device,
                cancellationToken).ConfigureAwait(false);
            if (IsCalendar(regions)) return regions;
            await Task.Delay(ObservationDelay, cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException("Reward Calendar did not open before its deadline.");
    }

    private async Task<IReadOnlyList<OcrTextRegion>> RequireCalendarAsync(
        string device,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            IReadOnlyList<OcrTextRegion> regions = await _observations.ObserveAsync(
                RewardGrid,
                device,
                cancellationToken).ConfigureAwait(false);
            if (IsCalendar(regions)) return regions;
            await Task.Delay(ObservationDelay, cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException("Reward Calendar changed before the next claim click.");
    }

    private static bool IsCalendar(IReadOnlyList<OcrTextRegion> regions) =>
        OcrRuleEngine.FindTarget(new OcrTargetRule("Reward Calendar", "reward calendar"), regions) is not null &&
        CalendarClaimPolicy.TryResolveClaimPoints(regions, ClientSize, out _);

}
