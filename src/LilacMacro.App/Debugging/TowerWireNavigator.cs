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

        if (!await _transitions.RunAsync(
                StoryWireStage.TowerStage,
                TowerWorkflowCatalog.TowerStage,
                DebugWorkflowCatalog.MatchPreview,
                options.Device,
                token => ClickSelectStageAsync(options.Device, token),
                progress,
                cancellationToken).ConfigureAwait(false))
            return Failed("TOWER STAGE BLOCKED");

        TowerPreviewSelection preview = await ReadPreviewAsync(
            options.Device,
            cancellationToken).ConfigureAwait(false);
        deepDebug.RecordEvent("tower", "stage_selected", new
        {
            Type = options.TowerType.ToString(),
            Map = preview.Map,
            Floor = preview.Floor,
            EvidenceState = "MatchPreview",
        });
        return new TowerNavigationResult(true, "TOWER STAGE SELECTED", preview.Map, preview.Floor);
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

    private async Task<ObservedStateTransitionActionResult> ClickSelectStageAsync(
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot snapshot = await _states.RunAsync(
            TowerWorkflowCatalog.TowerStage, device, cancellationToken).ConfigureAwait(false);
        if (!snapshot.Evaluation.IsMatch)
            throw new InvalidDataException("Tower stage was not verified.");
        OcrTargetMatch selectStage = snapshot.Evaluation.Matches
            .FirstOrDefault(match => match.Target == "Select Stage")
            ?? throw new InvalidDataException("Tower stage has no Select Stage action.");
        await workspace.ClickRobloxAsync(
            DebugWorkflowCatalog.ClientSize,
            selectStage.Region.Bounds.Center,
            cancellationToken).ConfigureAwait(false);
        return new ObservedStateTransitionActionResult(
            true,
            "SELECT STAGE",
            ["SELECT STAGE"]);
    }

    private async Task<TowerPreviewSelection> ReadPreviewAsync(
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot snapshot = await _states.RunAsync(
            TowerWorkflowCatalog.TowerPreviewMapFloor, device, cancellationToken).ConfigureAwait(false);
        if (!snapshot.Evaluation.IsMatch)
            throw new InvalidDataException("Tower Match Preview map and floor were not verified.");
        return TowerRunPolicy.ResolvePreview(snapshot.Regions, DebugWorkflowCatalog.MapTargets);
    }

    private static TowerNavigationResult Failed(string status) => new(false, status, null, 0);

}
