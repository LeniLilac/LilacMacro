using LilacMacro.App.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Debugging;

internal sealed class TowerWireNavigator(
    WorkspaceController workspace,
    OcrRunner ocr,
    DeepDebugSessionService deepDebug)
{
    private const int MaximumTypeAttempts = 3;
    private static readonly TimeSpan TypeSettleDelay = TimeSpan.FromMilliseconds(400);
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);
    private readonly WireTransitionService _transitions = new(workspace, ocr, deepDebug);

    public async Task<TowerNavigationResult> NavigateAsync(
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        if (!await SelectTypeAsync(options, progress, cancellationToken).ConfigureAwait(false))
            return Failed("TOWER TYPE BLOCKED");

        if (!await _transitions.RunAsync(
                StoryWireStage.TowerFloor,
                TowerWorkflowCatalog.TowerFloorList,
                TowerWorkflowCatalog.TowerStage,
                options.Device,
                token => ClickHighestFloorAsync(options.Device, token),
                progress,
                cancellationToken).ConfigureAwait(false))
            return Failed("TOWER FLOOR BLOCKED");

        string? map = null;
        int floor = 0;
        if (!await _transitions.RunAsync(
                StoryWireStage.TowerStage,
                TowerWorkflowCatalog.TowerStage,
                DebugWorkflowCatalog.MatchPreview,
                options.Device,
                async token =>
                {
                    TowerStageSelection selection = await ReadStageAsync(options.Device, token).ConfigureAwait(false);
                    map = selection.Map;
                    floor = selection.Floor;
                    await workspace.ClickRobloxAsync(
                        DebugWorkflowCatalog.ClientSize,
                        selection.SelectStagePoint,
                        token).ConfigureAwait(false);
                    return new ObservedStateTransitionActionResult(
                        true,
                        $"SELECT STAGE {selection.Map} FLOOR {selection.Floor}",
                        [$"MAP {selection.Map}", $"FLOOR {selection.Floor}"]);
                },
                progress,
                cancellationToken).ConfigureAwait(false))
            return Failed("TOWER STAGE BLOCKED");

        if (map is null || floor < 1)
            return Failed("TOWER STAGE EVIDENCE LOST");
        deepDebug.RecordEvent("tower", "stage_selected", new
        {
            Type = options.TowerType.ToString(),
            Map = map,
            Floor = floor,
        });
        return new TowerNavigationResult(true, "TOWER STAGE SELECTED", map, floor);
    }

    private async Task<bool> SelectTypeAsync(
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        string label = TowerRunPolicy.SelectionLabel(options.TowerType);
        string normalized = OcrRuleEngine.Normalize(label);
        for (int attempt = 1; attempt <= MaximumTypeAttempts; attempt++)
        {
            DebugOcrSnapshot snapshot = await _states.RunAsync(
                TowerWorkflowCatalog.TowerSelect,
                options.Device,
                cancellationToken).ConfigureAwait(false);
            if (!snapshot.Evaluation.IsMatch) return false;
            if (snapshot.Regions.Any(region =>
                    region.Bounds.Y < 140 &&
                    OcrRuleEngine.Normalize(region.Text) == normalized))
            {
                progress.Report(new StoryWireProgress(
                    StoryWireStage.TowerType,
                    StoryWireStageStatus.Passed,
                    $"{label.ToUpperInvariant()} VERIFIED",
                    [$"TYPE ATTEMPTS {attempt - 1}"]));
                return true;
            }

            OcrTextRegion? target = snapshot.Regions
                .Where(region =>
                    region.Bounds.X < 320 &&
                    region.Bounds.Y is > 120 and < 500 &&
                    OcrRuleEngine.Normalize(region.Text) == normalized)
                .OrderBy(region => region.Bounds.Y)
                .ThenBy(region => region.Bounds.X)
                .FirstOrDefault();
            if (target is null) return false;
            await workspace.ClickRobloxAsync(
                DebugWorkflowCatalog.ClientSize,
                target.Bounds.Center,
                cancellationToken).ConfigureAwait(false);
            await Task.Delay(TypeSettleDelay, cancellationToken).ConfigureAwait(false);
        }
        return false;
    }

    private async Task<ObservedStateTransitionActionResult> ClickHighestFloorAsync(
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot tower = await _states.RunAsync(
            TowerWorkflowCatalog.TowerSelect, device, cancellationToken).ConfigureAwait(false);
        if (!tower.Evaluation.IsMatch)
            return new ObservedStateTransitionActionResult(false, "TOWER SELECT NOT VERIFIED", []);
        DebugOcrSnapshot floors = await _states.RunAsync(
            TowerWorkflowCatalog.TowerFloorList, device, cancellationToken).ConfigureAwait(false);
        if (!floors.Evaluation.IsMatch)
            return new ObservedStateTransitionActionResult(false, "TOWER FLOOR LIST NOT VERIFIED", []);
        TowerFloorSelection? selection = TowerRunPolicy.SelectTopRightFloor(floors.Regions);
        if (selection is null)
            return new ObservedStateTransitionActionResult(false, "NO TOWER FLOOR TEXT", []);
        await workspace.ClickRobloxAsync(
            DebugWorkflowCatalog.ClientSize,
            selection.Region.Bounds.Center,
            cancellationToken).ConfigureAwait(false);
        return new ObservedStateTransitionActionResult(
            true,
            selection.Floor > 0 ? $"FLOOR {selection.Floor} CLICKED" : "TOP-RIGHT FLOOR LABEL CLICKED",
            [$"FLOOR BOUNDS {selection.Region.Bounds}"]);
    }

    private async Task<TowerStageSelection> ReadStageAsync(
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot snapshot = await _states.RunAsync(
            TowerWorkflowCatalog.TowerStage, device, cancellationToken).ConfigureAwait(false);
        if (!snapshot.Evaluation.IsMatch)
            throw new InvalidDataException("Tower stage was not verified.");
        HashSet<string> mapNames = DebugWorkflowCatalog.MapTargets
            .Select(target => target.Name)
            .ToHashSet(StringComparer.Ordinal);
        string map = snapshot.Evaluation.Matches
            .FirstOrDefault(match => mapNames.Contains(match.Target))?.Target
            ?? throw new InvalidDataException("Tower stage has no supported Story map.");
        TowerFloorSelection? floor = TowerRunPolicy.SelectTopRightFloor(snapshot.Regions);
        OcrTargetMatch selectStage = snapshot.Evaluation.Matches
            .FirstOrDefault(match => match.Target == "Select Stage")
            ?? throw new InvalidDataException("Tower stage has no Select Stage action.");
        return new TowerStageSelection(
            map,
            floor?.Floor ?? throw new InvalidDataException("Tower stage has no floor number."),
            selectStage.Region.Bounds.Center);
    }

    private static TowerNavigationResult Failed(string status) => new(false, status, null, 0);

    private sealed record TowerStageSelection(string Map, int Floor, PixelPoint SelectStagePoint);
}
