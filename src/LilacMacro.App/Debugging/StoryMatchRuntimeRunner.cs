using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Ocr;
using LilacMacro.Core.Placements;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Debugging;

internal sealed class StoryMatchRuntimeRunner(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private readonly PlacementPlaybackService _placements = new(workspace, ocr);
    private readonly MapPreparationService _mapPreparation = new(workspace);
    private readonly MatchWaveService _waves = new(workspace, ocr);
    private readonly ExpeditionSettingsService _settings = new(workspace, ocr);
    private readonly PlacementSetupStore _placementStore = new(ResolvePlacementRoot());

    public async Task<StoryWireTestResult> RunAsync(
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        bool alignCamera,
        CancellationToken cancellationToken)
    {
        progress.Report(new StoryWireProgress(
            StoryWireStage.MatchRuntime,
            StoryWireStageStatus.Running,
            "RUNNING",
            []));
        try
        {
            if (alignCamera)
            {
                await workspace.AlignCameraAsync(
                    DebugWorkflowCatalog.ClientSize,
                    options.ShiftLockVirtualKey,
                    cancellationToken);
                progress.Report(new StoryWireProgress(
                    StoryWireStage.MatchRuntime,
                    StoryWireStageStatus.Running,
                    "CAMERA ALIGNED",
                    ["CAMERA ALIGNED"]));
                await _mapPreparation.PrepareAsync(
                    ResolvePlacementMap(options).Id,
                    options.PlacementKeys.ReservedVirtualKey,
                    message => progress.Report(new StoryWireProgress(
                        StoryWireStage.MatchRuntime,
                        StoryWireStageStatus.Running,
                        message,
                        [message])),
                    cancellationToken);
            }

            PlacementMapDefinition map = ResolvePlacementMap(options);
            PlacementSetupDocument document = await _placementStore.LoadAsync(map.Id, cancellationToken);
            string routeId = options.GameMode switch
            {
                WireGameMode.Challenge => "challenge",
                WireGameMode.Tower => TowerRunPolicy.PlacementRouteId(options.TowerType),
                _ => RouteId(options.Act),
            };
            PlacementRouteDefinition routeDefinition = PlacementRouteCatalog.For(map)
                .FirstOrDefault(candidate => candidate.Id == routeId)
                ?? PlacementRouteCatalog.For(map).First(candidate => candidate.IsShared);
            PlacementRouteSetup route = PlacementRouteCatalog.EffectiveRoute(document, routeDefinition);
            if (options.GameMode == WireGameMode.Story && options.Act == StoryAct.Infinite)
            {
                int executed = await _placements.RunSetupAsync(
                    document,
                    route,
                    options.PlacementKeys,
                    options.Device,
                    message => progress.Report(new StoryWireProgress(
                        StoryWireStage.MatchRuntime,
                        StoryWireStageStatus.Running,
                        message,
                        [message])),
                    cancellationToken);
                await _waves.WaitForTargetAsync(
                    options.InfiniteWave,
                    options.Device,
                    message => progress.Report(new StoryWireProgress(
                        StoryWireStage.MatchRuntime,
                        StoryWireStageStatus.Running,
                        message,
                        [message])),
                    cancellationToken);
                await _settings.RestartAsync(
                    options.Device,
                    message => progress.Report(new StoryWireProgress(
                        StoryWireStage.MatchRuntime,
                        StoryWireStageStatus.Running,
                        message,
                        [message])),
                    cancellationToken);
                string infiniteStatus = $"WAVE {options.InfiniteWave} RESET VERIFIED";
                progress.Report(new StoryWireProgress(
                    StoryWireStage.MatchRuntime,
                    StoryWireStageStatus.Passed,
                    infiniteStatus,
                    [infiniteStatus, $"PLACEMENT STEPS {executed}"]));
                return new StoryWireTestResult(
                    true,
                    StoryWireStage.MatchRuntime,
                    infiniteStatus,
                    Outcome: MatchTerminalOutcome.Victory,
                    RepeatedPrestartReady: true);
            }
            PlacementRuntimeResult result = await _placements.RunAsync(
                document,
                route,
                options.PlacementKeys,
                options.Device,
                options.RepeatStage,
                RaidDropDismissalPolicy.IsEnabled(options.GameMode, options.Act),
                TimeSpan.FromMinutes(30),
                message => progress.Report(new StoryWireProgress(
                    StoryWireStage.MatchRuntime,
                    StoryWireStageStatus.Running,
                    message,
                    [message])),
                cancellationToken);
            string status = $"{result.Outcome.ToString().ToUpperInvariant()} VERIFIED";
            progress.Report(new StoryWireProgress(
                StoryWireStage.MatchRuntime,
                StoryWireStageStatus.Passed,
                status,
                [status]));
            return new StoryWireTestResult(
                true,
                StoryWireStage.MatchRuntime,
                status,
                Outcome: result.Outcome);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or TimeoutException)
        {
            progress.Report(new StoryWireProgress(
                StoryWireStage.MatchRuntime,
                StoryWireStageStatus.Failed,
                error.Message,
                [error.Message]));
            return new StoryWireTestResult(false, StoryWireStage.MatchRuntime, "MATCH RUNTIME BLOCKED");
        }
    }

    public async Task RepeatStageAsync(
        MatchTerminalOutcome outcome,
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        if (!WireGameModeRepeatPolicy.Supports(options.GameMode))
            throw new InvalidOperationException($"{options.GameMode} cannot continue through Repeat Stage.");

        progress.Report(new StoryWireProgress(
            StoryWireStage.MatchRuntime,
            StoryWireStageStatus.Running,
            "REPEAT STAGE",
            ["REPEAT STAGE"]));
        await _placements.RepeatAsync(outcome, options.Device, cancellationToken);
        progress.Report(new StoryWireProgress(
            StoryWireStage.MatchRuntime,
            StoryWireStageStatus.Passed,
            "REPEAT STAGE VERIFIED + CLICKED",
            ["REPEAT STAGE VERIFIED + CLICKED"]));
    }

    public async Task RepeatTowerFloorAsync(
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        if (options.GameMode != WireGameMode.Tower)
            throw new InvalidOperationException("Repeat Floor is only available for Tower tasks.");
        progress.Report(new StoryWireProgress(
            StoryWireStage.MatchRuntime,
            StoryWireStageStatus.Running,
            "REPEAT FLOOR",
            ["REPEAT FLOOR"]));
        await _placements.RepeatFloorAsync(options.Device, cancellationToken);
        progress.Report(new StoryWireProgress(
            StoryWireStage.MatchRuntime,
            StoryWireStageStatus.Passed,
            "REPEAT FLOOR VERIFIED + CLICKED",
            ["REPEAT FLOOR VERIFIED + CLICKED"]));
    }

    private static PlacementMapDefinition ResolvePlacementMap(StoryWireTestOptions options)
    {
        string id = options.GameMode switch
        {
            WireGameMode.Raid => $"raid-spirit-city-{RouteId(options.Act)}",
            WireGameMode.Event => EventRunPolicy.MapId(options.Map, options.Act),
            _ => $"story-{Slug(options.Map)}",
        };
        return PlacementMapCatalog.Definitions.First(candidate => candidate.Id == id);
    }

    private static string RouteId(StoryAct act) => act switch
    {
        StoryAct.Act1 => "act-1",
        StoryAct.Act2 => "act-2",
        StoryAct.Act3 => "act-3",
        StoryAct.Act4 => "act-4",
        StoryAct.Act5 => "act-5",
        StoryAct.Infinite => "infinite",
        StoryAct.Mastery => "mastery",
        _ => throw new ArgumentOutOfRangeException(nameof(act)),
    };

    private static string Slug(string value) =>
        value.ToLowerInvariant().Replace("'", string.Empty).Replace(' ', '-');

    private static string ResolvePlacementRoot() =>
        Environment.GetEnvironmentVariable("LILACMACRO_RUNNER_PLACEMENTS") is { Length: > 0 } value
            ? Path.GetFullPath(value)
            : Path.Combine(
                Environment.GetEnvironmentVariable("LILACMACRO_CONFIGURATION_ROOT")
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LilacMacro"),
                "placements");
}
