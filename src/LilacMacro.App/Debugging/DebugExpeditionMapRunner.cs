using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;
using static LilacMacro.App.Debugging.DebugReportFactory;

namespace LilacMacro.App.Debugging;

internal sealed class DebugExpeditionMapRunner(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private const int NormalizeClickCount = 3;
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);

    public async Task<DebugRunReport> CheckAsync(
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot snapshot = await RunStateAsync(device, cancellationToken);
        if (!snapshot.Evaluation.IsMatch) return FailedState(snapshot);

        ExpeditionMapPickerLayout? layout = CreateLayout(snapshot);
        return layout is null
            ? MissingControls(snapshot)
            : new DebugRunReport(
                snapshot,
                true,
                "EXPEDITION MAP TRUE",
                [StateLine(snapshot), LayoutLine(layout)]);
    }

    public async Task<DebugRunReport> SelectAsync(
        string map,
        int difficulty,
        string device,
        CancellationToken cancellationToken)
    {
        int increaseClicks = ExpeditionMapPickerLayout.GetIncreaseClickCount(difficulty);
        OcrTargetRule mapRule = FindMapRule(map);
        DebugOcrSnapshot initial = await RunStateAsync(device, cancellationToken);
        if (!initial.Evaluation.IsMatch) return FailedState(initial);

        OcrTargetMatch? target = OcrRuleEngine.FindLeftmostTarget(mapRule, initial.Regions);
        if (target is null) return MissingTarget(initial, map.ToUpperInvariant());
        PixelPoint mapPoint = target.Region.Bounds.Center;
        await workspace.ClickRobloxAsync(
            DebugWorkflowCatalog.ClientSize,
            mapPoint,
            cancellationToken);
        await Task.Delay(250, cancellationToken);

        DebugOcrSnapshot confirmation = await RunStateAsync(device, cancellationToken);
        if (!confirmation.Evaluation.IsMatch)
        {
            return BlockedAfterMap(
                confirmation,
                "EXPEDITION MAP FALSE",
                initial,
                map,
                mapPoint);
        }

        OcrTargetMatch? selectedMap = ExpeditionMapPickerLayout.FindSelectedMap(
            mapRule,
            confirmation.Regions,
            target.Region.Bounds);
        if (selectedMap is null)
        {
            return BlockedAfterMap(
                confirmation,
                $"{map.ToUpperInvariant()} NOT CONFIRMED",
                initial,
                map,
                mapPoint);
        }

        ExpeditionMapPickerLayout? layout = CreateLayout(confirmation);
        if (layout is null)
        {
            return BlockedAfterMap(
                confirmation,
                "EXPEDITION CONTROLS MISSING",
                initial,
                map,
                mapPoint);
        }

        await ClickRepeatedAsync(layout.MinusPoint, NormalizeClickCount, cancellationToken);
        await ClickRepeatedAsync(layout.PlusPoint, increaseClicks, cancellationToken);
        await workspace.ClickRobloxAsync(
            DebugWorkflowCatalog.ClientSize,
            layout.SelectStagePoint,
            cancellationToken);

        return new DebugRunReport(
            confirmation,
            true,
            $"{map.ToUpperInvariant()} D{difficulty} + SELECT CLICKED",
            [
                StateLine(initial),
                $"{map.ToUpperInvariant()} [{mapPoint.X},{mapPoint.Y}] LEFTMOST CENTER",
                "WAIT 250 MS",
                StateLine(confirmation),
                $"CONFIRMED [{selectedMap.Region.Bounds.X},{selectedMap.Region.Bounds.Y}," +
                    $"{selectedMap.Region.Bounds.Width},{selectedMap.Region.Bounds.Height}]",
                LayoutLine(layout),
                $"MINUS [{layout.MinusPoint.X},{layout.MinusPoint.Y}] DERIVED x{NormalizeClickCount}",
                $"PLUS [{layout.PlusPoint.X},{layout.PlusPoint.Y}] DERIVED x{increaseClicks}",
                $"SELECT STAGE [{layout.SelectStagePoint.X},{layout.SelectStagePoint.Y}] CENTER",
            ]);
    }

    private async Task ClickRepeatedAsync(
        PixelPoint point,
        int count,
        CancellationToken cancellationToken)
    {
        for (int click = 0; click < count; click++)
        {
            await workspace.ClickRobloxAsync(
                DebugWorkflowCatalog.ClientSize,
                point,
                cancellationToken);
        }
    }

    private Task<DebugOcrSnapshot> RunStateAsync(
        string device,
        CancellationToken cancellationToken) => _states.RunAsync(
        DebugWorkflowCatalog.ExpeditionMap,
        device,
        cancellationToken);

    private static ExpeditionMapPickerLayout? CreateLayout(DebugOcrSnapshot snapshot) =>
        ExpeditionMapPickerLayout.TryCreate(snapshot.Regions, DebugWorkflowCatalog.ClientSize);

    private static OcrTargetRule FindMapRule(string map) =>
        DebugWorkflowCatalog.ExpeditionMapTargets.SingleOrDefault(
            target => target.Name.Equals(map, StringComparison.Ordinal))
        ?? throw new ArgumentOutOfRangeException(nameof(map), map, "Unknown Expedition map.");

    private static DebugRunReport MissingControls(DebugOcrSnapshot snapshot) => new(
        snapshot,
        false,
        "EXPEDITION CONTROLS MISSING",
        [StateLine(snapshot), "TOP DIFFICULTY + SELECT STAGE REQUIRED", "INPUT BLOCKED"]);

    private static DebugRunReport BlockedAfterMap(
        DebugOcrSnapshot confirmation,
        string status,
        DebugOcrSnapshot initial,
        string map,
        PixelPoint mapPoint) => new(
        confirmation,
        false,
        status,
        [
            StateLine(initial),
            $"{map.ToUpperInvariant()} [{mapPoint.X},{mapPoint.Y}] LEFTMOST CENTER",
            "WAIT 250 MS",
            StateLine(confirmation),
            "DIFFICULTY INPUT BLOCKED",
        ]);

    private static string LayoutLine(ExpeditionMapPickerLayout layout) =>
        $"DIFFICULTY [{layout.DifficultyBounds.X},{layout.DifficultyBounds.Y}," +
        $"{layout.DifficultyBounds.Width},{layout.DifficultyBounds.Height}] SELECT STAGE " +
        $"[{layout.SelectStageBounds.X},{layout.SelectStageBounds.Y}," +
        $"{layout.SelectStageBounds.Width},{layout.SelectStageBounds.Height}] SPAN {layout.VerticalSpan}";
}
