using System.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Placements;

namespace LilacMacro.App.Debugging;

internal sealed class ExpeditionMatchRuntimeRunner(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private static readonly PixelPoint IdleRewardPoint = new(1009, 345);
    private readonly PlacementPlaybackService _placements = new(workspace, ocr);
    private readonly PlacementSetupStore _placementStore = new(ResolvePlacementRoot());
    private readonly ExpeditionNodeEvidenceService _nodes = new(workspace, ocr);
    private readonly ExpeditionCheckpointService _checkpoint = new(workspace, ocr);
    private readonly ExpeditionEncounterService _encounter = new(workspace, ocr);
    private readonly ExpeditionRewardPoolService _rewards = new(workspace, ocr);
    private readonly ExpeditionRewardProfileStore _rewardProfiles = new();
    private readonly ExpeditionSettingsService _settings = new(workspace, ocr);
    private readonly MatchTerminalService _terminal = new(workspace, ocr);
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);

    public async Task<StoryWireTestResult> RunAsync(
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        bool alignCamera,
        CancellationToken cancellationToken)
    {
        Action<string> report = message => progress.Report(new StoryWireProgress(
            StoryWireStage.MatchRuntime, StoryWireStageStatus.Running, message, [message]));
        try
        {
            await WaitForMatchArrivalAsync(options.Device, report, cancellationToken)
                .ConfigureAwait(false);

            if (alignCamera)
            {
                await workspace.AlignCameraAsync(
                    DebugWorkflowCatalog.ClientSize,
                    options.ShiftLockVirtualKey,
                    cancellationToken).ConfigureAwait(false);
                report("CAMERA ALIGNED");
            }

            (PlacementSetupDocument document, PlacementRouteSetup route) = await LoadPlacementAsync(
                options.Map, cancellationToken).ConfigureAwait(false);
            await OptimizeRouteAsync(options, report, cancellationToken).ConfigureAwait(false);
            ExpeditionPlacementSession placement = await _placements.RunExpeditionInitialAsync(
                document, route, options.PlacementKeys, options.Device, report, cancellationToken)
                .ConfigureAwait(false);
            await _checkpoint.ContinueAsync(options.Device, report, cancellationToken).ConfigureAwait(false);

            ExpeditionRunTracker tracker = new(options.ExtractAtCheckpoint, options.BossesBeforeExtract);
            ExpeditionDefenseStartEpisodeTracker defenseStartEpisode = new();
            _nodes.ResetForMatch();
            int semanticRevision = _nodes.SemanticRevision;
            Stopwatch progressWatchdog = Stopwatch.StartNew();
            Stopwatch liveControlProbe = Stopwatch.StartNew();
            bool initialLiveControlProbe = true;
            ExpeditionNodeType? candidate = null;
            int stable = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ExpeditionProgressPolicy.HasStalled(progressWatchdog.Elapsed))
                {
                    throw new TimeoutException(
                        "Expedition produced no verified state transition for five minutes.");
                }
                MatchTerminalOutcome? terminal = await _terminal.TryObserveAsync(
                    options.Device, cancellationToken).ConfigureAwait(false);
                if (terminal is MatchTerminalOutcome outcome) return Passed(outcome, progress);

                if (initialLiveControlProbe || liveControlProbe.ElapsedMilliseconds >=
                    ExpeditionLiveControlPolicy.ProbeIntervalMilliseconds)
                {
                    initialLiveControlProbe = false;
                    liveControlProbe.Restart();
                    ExpeditionLiveControl control = await _checkpoint.ObserveLiveControlAsync(
                        options.Device, report, cancellationToken).ConfigureAwait(false);
                    if (control == ExpeditionLiveControl.Checkpoint)
                    {
                        candidate = null;
                        stable = 0;
                        ExpeditionNodeAction checkpointAction = tracker.ObserveCheckpointSource();
                        report($"CHECKPOINT LIVE CONTROL | {checkpointAction.ToString().ToUpperInvariant()}");
                        await RunActionAsync(
                            checkpointAction, placement, options, report, cancellationToken)
                            .ConfigureAwait(false);
                        progressWatchdog.Restart();
                        if (checkpointAction == ExpeditionNodeAction.Extract)
                        {
                            MatchTerminalOutcome extractionOutcome = await WaitTerminalWithIdleRewardAsync(
                                options.Device, report, cancellationToken).ConfigureAwait(false);
                            return Passed(extractionOutcome, progress);
                        }
                        continue;
                    }

                    if (control == ExpeditionLiveControl.Encounter)
                    {
                        candidate = null;
                        stable = 0;
                        tracker.Observe(ExpeditionNodeType.Encounter);
                        report("ENCOUNTER LIVE CONTROL | CONTINUE");
                        await _encounter.RunAsync(
                            options.Device, report, cancellationToken).ConfigureAwait(false);
                        progressWatchdog.Restart();
                        continue;
                    }
                }

                ExpeditionNodeType? observed = await _nodes.ObserveAsync(
                    options.Device, report, cancellationToken).ConfigureAwait(false);
                if (_nodes.SemanticRevision != semanticRevision)
                {
                    semanticRevision = _nodes.SemanticRevision;
                    progressWatchdog.Restart();
                    report("EXPEDITION SEMANTIC NODE PROGRESS VERIFIED");
                }
                if (observed is null)
                {
                    candidate = null;
                    stable = 0;
                }
                else if (candidate == observed)
                {
                    stable++;
                }
                else
                {
                    candidate = observed;
                    stable = 1;
                }

                if (candidate is ExpeditionNodeType node && stable >= 2)
                {
                    ExpeditionNodeAction action = ExpeditionNodeAction.Wait;
                    if (node is ExpeditionNodeType.Defense or ExpeditionNodeType.Elite)
                    {
                        DebugOcrSnapshot prestart = await _states.RunAsync(
                            DebugWorkflowCatalog.MatchPrestart,
                            options.Device,
                            cancellationToken).ConfigureAwait(false);
                        if (defenseStartEpisode.Observe(prestart.Evaluation.IsMatch))
                        {
                            tracker.Observe(node);
                            action = ExpeditionNodeAction.ReplayPlacementsAndStart;
                            report("NEW DEFENSE START GAME EPISODE VERIFIED");
                        }
                    }
                    else
                    {
                        defenseStartEpisode.Observe(startGameVisible: false);
                        if (ExpeditionLiveControlPolicy.RequiresLiveControlEvidence(node))
                        {
                            report($"{node.ToString().ToUpperInvariant()} SEMANTIC EVIDENCE | WAITING FOR LIVE CONTROLS");
                        }
                        else
                        {
                            action = tracker.Observe(node);
                        }
                    }

                    stable = 0;
                    await RunActionAsync(action, placement, options, report, cancellationToken)
                        .ConfigureAwait(false);
                    if (action == ExpeditionNodeAction.ReplayPlacementsAndStart)
                    {
                        defenseStartEpisode.MarkHandled();
                        progressWatchdog.Restart();
                    }
                    if (action == ExpeditionNodeAction.Extract)
                    {
                        MatchTerminalOutcome extractionOutcome = await WaitTerminalWithIdleRewardAsync(
                            options.Device, report, cancellationToken).ConfigureAwait(false);
                        return Passed(extractionOutcome, progress);
                    }
                }

                await workspace.ClickRobloxAsync(
                    DebugWorkflowCatalog.ClientSize, IdleRewardPoint, cancellationToken).ConfigureAwait(false);
                report("EXPEDITION IDLE REWARD CLICK");
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or TimeoutException)
        {
            progress.Report(new StoryWireProgress(
                StoryWireStage.MatchRuntime,
                StoryWireStageStatus.Failed,
                error.Message,
                [error.Message]));
            return new StoryWireTestResult(false, StoryWireStage.MatchRuntime, "EXPEDITION RUNTIME BLOCKED");
        }
    }

    private async Task WaitForMatchArrivalAsync(
        string device,
        Action<string> report,
        CancellationToken cancellationToken)
    {
        report("WAITING FOR EXPEDITION MATCH ARRIVAL");
        DebugOcrSnapshot prestart = await _states.WaitForMatchAsync(
            DebugWorkflowCatalog.MatchPrestart,
            device,
            maximumObservations: 120,
            retryDelay: TimeSpan.FromSeconds(1),
            cancellationToken).ConfigureAwait(false);
        if (!prestart.Evaluation.IsMatch)
        {
            throw new TimeoutException(
                "Expedition did not expose the visible Start Game prompt within two minutes.");
        }
        report("EXPEDITION MATCH ARRIVAL VERIFIED");
    }

    private async Task OptimizeRouteAsync(
        StoryWireTestOptions options,
        Action<string> report,
        CancellationToken cancellationToken)
    {
        ExpeditionRewardResource target = ExpeditionRewardPolicy.ParseResource(
            options.ExpeditionRewardTarget);
        if (target == ExpeditionRewardResource.None)
        {
            await _rewards.StartGameForRouteAsync(options.Device, cancellationToken).ConfigureAwait(false);
            report("EXPEDITION STARTED");
            return;
        }

        int rerolls = 0;
        bool routeOpen = false;
        Stopwatch? reroll = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!routeOpen)
                await _rewards.OpenAsync(options.Device, cancellationToken).ConfigureAwait(false);
            routeOpen = false;
            ExpeditionRewardObservation observation;
            try
            {
                observation = await _rewards.ObserveAsync(
                    target, options.Device, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException error) when (
                error.Message.StartsWith("Expedition route reward '", StringComparison.Ordinal))
            {
                report("EXPEDITION REWARD READ MISS | IN-MATCH REROLL");
                await _rewards.BackToPrestartAfterReadFailureAsync(
                    options.Device, cancellationToken).ConfigureAwait(false);
                reroll = Stopwatch.StartNew();
                await _rewards.StartGameForRouteAsync(options.Device, cancellationToken).ConfigureAwait(false);
                await _settings.RestartForRouteRerollAsync(
                    options.Device, report, cancellationToken).ConfigureAwait(false);
                await _rewards.OpenAfterRestartAsync(options.Device, cancellationToken).ConfigureAwait(false);
                routeOpen = true;
                rerolls++;
                continue;
            }
            if (reroll is not null)
            {
                reroll.Stop();
                await _rewardProfiles.RecordRerollAsync(
                    options.Device, reroll.Elapsed, cancellationToken).ConfigureAwait(false);
            }
            if (observation.CompletePool)
            {
                await _rewardProfiles.RecordPoolAsync(
                    options.ExpeditionDifficulty, observation.Pool, cancellationToken).ConfigureAwait(false);
            }
            ExpeditionRewardOptimization optimization = await _rewardProfiles.OptimizeAsync(
                options.ExpeditionDifficulty, target, options.Device, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException(
                    $"Difficulty {options.ExpeditionDifficulty} reward optimization needs " +
                    $"{ExpeditionRewardPolicy.MinimumOptimizationSamples} Runtime Lab pools.");
            int quantity = observation.Pool.Quantity(target);
            bool accepted = quantity >= optimization.Threshold;
            report($"EXPEDITION REWARD {target.ToString().ToUpperInvariant()} {quantity}/{optimization.Threshold} | " +
                (accepted ? "ACCEPT" : $"REROLL {rerolls + 1}"));
            if (!accepted) reroll = Stopwatch.StartNew();
            await _rewards.BackToPrestartAsync(
                observation, options.Device, cancellationToken).ConfigureAwait(false);

            if (accepted)
            {
                await _rewards.StartGameForRouteAsync(options.Device, cancellationToken).ConfigureAwait(false);
                report("EXPEDITION STARTED");
                return;
            }
            await _rewards.StartGameForRouteAsync(options.Device, cancellationToken).ConfigureAwait(false);
            await _settings.RestartForRouteRerollAsync(
                options.Device, report, cancellationToken).ConfigureAwait(false);
            await _rewards.OpenAfterRestartAsync(options.Device, cancellationToken).ConfigureAwait(false);
            routeOpen = true;
            rerolls++;
        }
    }

    private async Task RunActionAsync(
        ExpeditionNodeAction action,
        ExpeditionPlacementSession placement,
        StoryWireTestOptions options,
        Action<string> report,
        CancellationToken cancellationToken)
    {
        switch (action)
        {
            case ExpeditionNodeAction.ReplayPlacementsAndStart:
                await WaitForDefensePrestartAsync(options.Device, report, cancellationToken)
                    .ConfigureAwait(false);
                await _placements.ReplayExpeditionAsync(
                    placement, options.PlacementKeys, options.Device, report, cancellationToken).ConfigureAwait(false);
                await _placements.SatisfyExpeditionStartBoundaryAsync(
                    placement, options.Device, report, cancellationToken).ConfigureAwait(false);
                report("EXPEDITION START GAME CLICKED");
                break;
            case ExpeditionNodeAction.RunEncounter:
                await _encounter.RunAsync(options.Device, report, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case ExpeditionNodeAction.Continue:
                await _checkpoint.ContinueAfterArrivalAsync(
                    options.Device, report, cancellationToken).ConfigureAwait(false);
                break;
            case ExpeditionNodeAction.Extract:
                await _checkpoint.ExtractAsync(options.Device, report, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task WaitForDefensePrestartAsync(
        string device,
        Action<string> report,
        CancellationToken cancellationToken)
    {
        report("WAITING FOR DEFENSE START GAME");
        DebugOcrSnapshot prestart = await _states.WaitForMatchAsync(
            DebugWorkflowCatalog.MatchPrestart,
            device,
            ExpeditionDefenseStartPolicy.ArrivalMaximumObservations,
            TimeSpan.FromMilliseconds(ExpeditionDefenseStartPolicy.ArrivalRetryMilliseconds),
            cancellationToken).ConfigureAwait(false);
        if (!prestart.Evaluation.IsMatch)
        {
            throw new TimeoutException(
                "Expedition Defense/Elite node did not expose the visible Start Game prompt.");
        }
        report("DEFENSE START GAME VERIFIED; REPLAYING PLACEMENTS");
    }

    private async Task<MatchTerminalOutcome> WaitTerminalWithIdleRewardAsync(
        string device,
        Action<string> report,
        CancellationToken cancellationToken)
    {
        for (int observation = 0; observation < 300; observation++)
        {
            MatchTerminalOutcome? terminal = await _terminal.TryObserveAsync(device, cancellationToken)
                .ConfigureAwait(false);
            if (terminal is MatchTerminalOutcome outcome) return outcome;
            await workspace.ClickRobloxAsync(
                DebugWorkflowCatalog.ClientSize, IdleRewardPoint, cancellationToken).ConfigureAwait(false);
            report("EXPEDITION IDLE REWARD CLICK");
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException("Expedition extraction did not reach Victory or Defeat within five minutes.");
    }

    private async Task<(PlacementSetupDocument, PlacementRouteSetup)> LoadPlacementAsync(
        string mapName,
        CancellationToken cancellationToken)
    {
        PlacementMapDefinition map = PlacementMapCatalog.Definitions.First(candidate =>
            candidate.Id == $"expedition-{Slug(mapName)}");
        PlacementSetupDocument document = await _placementStore.LoadAsync(map.Id, cancellationToken)
            .ConfigureAwait(false);
        PlacementRouteDefinition route = PlacementRouteCatalog.For(map).First(candidate => candidate.IsShared);
        return (document, PlacementRouteCatalog.EffectiveRoute(document, route));
    }

    private static StoryWireTestResult Passed(
        MatchTerminalOutcome outcome,
        IProgress<StoryWireProgress> progress)
    {
        string status = $"{outcome.ToString().ToUpperInvariant()} VERIFIED";
        progress.Report(new StoryWireProgress(
            StoryWireStage.MatchRuntime, StoryWireStageStatus.Passed, status, [status]));
        return new StoryWireTestResult(true, StoryWireStage.MatchRuntime, status, Outcome: outcome);
    }

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
