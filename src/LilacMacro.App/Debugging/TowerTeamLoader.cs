using LilacMacro.App.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Placements;

namespace LilacMacro.App.Debugging;

internal sealed class TowerTeamLoader(
    WorkspaceController workspace,
    OcrRunner ocr,
    DeepDebugSessionService deepDebug)
{
    private readonly DebugOcrController _debug = new(workspace, ocr);
    private readonly WireStateService _state = new(workspace, deepDebug);
    private readonly WireTransitionService _transitions = new(workspace, ocr, deepDebug);
    private readonly TowerPlacementResolver _placements = new(new PlacementSetupStore(ResolvePlacementRoot()));

    public async Task<StoryWireTestOptions?> LoadAsync(
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        if (options.NavigationKeys.UnitInventory is not int unitInventoryKey) return null;
        int team = await _placements.ResolveTeamAsync(
            options.Map, options.TowerType, cancellationToken).ConfigureAwait(false);
        options = options with { TeamNumber = team };

        if (!await TransitionAsync(
                StoryWireStage.Units,
                DebugWorkflowCatalog.MatchPrestart,
                DebugWorkflowCatalog.UnitInventory,
                token => PressKeyAsync(StoryWireStage.Units, unitInventoryKey, options, progress, token),
                options, progress, cancellationToken).ConfigureAwait(false) ||
            !await TransitionAsync(
                StoryWireStage.Teams,
                DebugWorkflowCatalog.UnitInventory,
                DebugWorkflowCatalog.TeamSwap,
                async token => ObservedStateTransitionActionResult.From(
                    await _debug.OpenTeamsAsync(options.Device, token).ConfigureAwait(false)),
                options, progress, cancellationToken).ConfigureAwait(false) ||
            !await LoadSelectedTeamAsync(options, unitInventoryKey, progress, cancellationToken).ConfigureAwait(false))
            return null;

        return options;
    }

    private async Task<bool> LoadSelectedTeamAsync(
        StoryWireTestOptions options,
        int unitInventoryKey,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        if (!await _state.ActAsync(
                StoryWireStage.LoadTeam,
                token => _debug.LoadTeamAsync(options.TeamNumber, options.Device, token),
                progress,
                cancellationToken).ConfigureAwait(false)) return false;
        if (!await _state.WaitAsync(
                StoryWireStage.LoadTeam,
                DebugWorkflowCatalog.TeamSwap,
                token => _debug.CheckTeamSwapAsync(options.Device, token),
                options.Mode,
                progress,
                cancellationToken).ConfigureAwait(false)) return false;
        return await TransitionAsync(
            StoryWireStage.LoadTeam,
            DebugWorkflowCatalog.TeamSwap,
            DebugWorkflowCatalog.MatchPrestart,
            token => PressKeyAsync(StoryWireStage.LoadTeam, unitInventoryKey, options, progress, token),
            options, progress, cancellationToken).ConfigureAwait(false);
    }

    private Task<bool> TransitionAsync(
        StoryWireStage stage,
        DebugStateSpec source,
        DebugStateSpec destination,
        Func<CancellationToken, Task<ObservedStateTransitionActionResult>> action,
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken) =>
        _transitions.RunAsync(
            stage, source, destination, options.Device, action, progress, cancellationToken);

    private async Task<ObservedStateTransitionActionResult> PressKeyAsync(
        StoryWireStage stage,
        int virtualKey,
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        string keyName = KeyboardKey.GetDisplayName(virtualKey).ToUpperInvariant();
        progress.Report(new StoryWireProgress(stage, StoryWireStageStatus.Running, $"KEY {keyName}", []));
        deepDebug.RecordEvent("wire", "navigation_key_started", new { Stage = StoryWireTestRunner.Format(stage), Key = keyName });
        AutomationKeyPress press = AutomationKeyPress.Create(
            virtualKey, 80, options.PlacementKeys.ReservedVirtualKey);
        await workspace.RunKeySequenceAsync(
            DebugWorkflowCatalog.ClientSize,
            AutomationKeySequence.Create([press]),
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        deepDebug.RecordEvent("wire", "navigation_key_completed", new { Stage = StoryWireTestRunner.Format(stage), Key = keyName });
        return new ObservedStateTransitionActionResult(true, $"KEY {keyName} SENT", [$"KEY {keyName}"]);
    }

    private static string ResolvePlacementRoot() =>
        Environment.GetEnvironmentVariable("LILACMACRO_RUNNER_PLACEMENTS") is { Length: > 0 } value
            ? Path.GetFullPath(value)
            : Path.Combine(
                Environment.GetEnvironmentVariable("LILACMACRO_CONFIGURATION_ROOT")
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LilacMacro"),
                "placements");
}
