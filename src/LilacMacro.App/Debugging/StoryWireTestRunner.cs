using LilacMacro.App.Infrastructure;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Placements;
using LilacMacro.Core.Ocr;
using LilacMacro.App.Runtime;

namespace LilacMacro.App.Debugging;

internal sealed class StoryWireTestRunner(
    WorkspaceController workspace,
    OcrRunner ocr,
    DeepDebugSessionService deepDebug)
{
    private readonly DebugOcrController _debug = new(workspace, ocr);
    private readonly DebugLobbyRunner _lobby = new(workspace, ocr);
    private readonly ChallengeWireNavigator _challenge = new(workspace, ocr, deepDebug);
    private readonly TowerWireNavigator _tower = new(workspace, ocr, deepDebug);
    private readonly TowerTeamLoader _towerTeam = new(workspace, ocr, deepDebug);
    private readonly EventWireNavigator _event = new(workspace, ocr);
    private readonly WireStateService _state = new(workspace, deepDebug);
    private readonly WireTransitionService _transitions = new(workspace, ocr, deepDebug);
    private readonly StoryMatchRuntimeRunner _matchRuntime = new(workspace, ocr);
    private readonly ExpeditionMatchRuntimeRunner _expeditionRuntime = new(workspace, ocr);

    public async Task<StoryWireTestResult> RunAsync(
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(progress);
        await _debug.PrepareAsync(cancellationToken);

        if (!await CheckAsync(StoryWireStage.Lobby, DebugWorkflowCatalog.Lobby, _debug.CheckLobbyAsync, options, progress, cancellationToken))
            return Failed(StoryWireStage.Lobby);
        if (options.GameMode == WireGameMode.Tower)
        {
            progress.Report(new StoryWireProgress(
                StoryWireStage.LoadTeam,
                StoryWireStageStatus.Waiting,
                "TEAM DEFERRED UNTIL TOWER MAP IS KNOWN",
                []));
        }
        else if (options.SkipTeamLoad)
        {
            progress.Report(new StoryWireProgress(
                StoryWireStage.LoadTeam,
                StoryWireStageStatus.Passed,
                $"TEAM {options.TeamNumber} RETAINED FROM THIS MACRO RUN",
                [$"TEAM {options.TeamNumber} RETAINED"]));
        }
        else
        {
            if (!await OpenNavigationAsync(
                    StoryWireStage.Units,
                    options.NavigationKeys.UnitInventory,
                    token => _lobby.OpenUnitsAsync(options.Device, token),
                    DebugWorkflowCatalog.Lobby,
                    DebugWorkflowCatalog.UnitInventory,
                    options,
                    progress,
                    cancellationToken))
                return Failed(StoryWireStage.Units);
            if (!await TransitionAsync(
                    StoryWireStage.Teams,
                    DebugWorkflowCatalog.UnitInventory,
                    DebugWorkflowCatalog.TeamSwap,
                    token => _debug.OpenTeamsAsync(options.Device, token),
                    options,
                    progress,
                    cancellationToken))
                return Failed(StoryWireStage.Teams);
            if (!await LoadTeamAsync(options, DebugWorkflowCatalog.Lobby, progress, cancellationToken))
                return Failed(StoryWireStage.LoadTeam);
        }
        if (options.GameMode == WireGameMode.Event)
        {
            if (!await OpenNavigationAsync(
                    StoryWireStage.Play,
                    null,
                    token => _debug.OpenEventsAsync(options.Device, token),
                    DebugWorkflowCatalog.Lobby,
                    DebugWorkflowCatalog.EventSelect,
                    options,
                    progress,
                    cancellationToken) ||
                !await TransitionAsync(
                    StoryWireStage.StoryMap,
                    DebugWorkflowCatalog.EventSelect,
                    DebugWorkflowCatalog.EventPageConfirm,
                    token => _debug.SelectEventAsync(EventDestination.VillainInvasion, options.Device, token),
                    options, progress, cancellationToken) ||
                !await TransitionAsync(
                    StoryWireStage.StoryAct,
                    DebugWorkflowCatalog.EventPageConfirm,
                    DebugWorkflowCatalog.EventActPicker,
                    token => _event.OpenVillainActsAsync(options.Device, token),
                    options, progress, cancellationToken) ||
                !await TransitionAsync(
                    StoryWireStage.MatchPreview,
                    DebugWorkflowCatalog.EventActPicker,
                    DebugWorkflowCatalog.MatchPreview,
                    token => _event.SelectActAndStageAsync(options.Act, options.Device, token),
                    options, progress, cancellationToken))
                return Failed(StoryWireStage.MatchPreview);
        }
        else
        {
            if (!await OpenNavigationAsync(
                    StoryWireStage.Play,
                    options.NavigationKeys.PlayMenu,
                    token => _debug.OpenPlayAsync(options.Device, token),
                    DebugWorkflowCatalog.Lobby,
                    DebugWorkflowCatalog.PlayUi,
                    options,
                    progress,
                    cancellationToken))
                return Failed(StoryWireStage.Play);
        }
        if (options.GameMode == WireGameMode.Tower)
        {
            if (!await TransitionAsync(
                    StoryWireStage.TowerType,
                    DebugWorkflowCatalog.PlayUi,
                    TowerWorkflowCatalog.TowerSelect,
                    token => _debug.SelectModeAsync("Tower", options.Device, token),
                    options, progress, cancellationToken))
                return Failed(StoryWireStage.TowerType);
            TowerNavigationResult tower = await _tower.NavigateAsync(options, progress, cancellationToken);
            if (!tower.Succeeded || tower.Map is null) return Failed(StoryWireStage.TowerStage);
            options = options with { Map = tower.Map, TowerFloor = tower.Floor };
        }
        else if (options.GameMode == WireGameMode.Challenge)
        {
            ChallengeNavigationResult challenge = await NavigateChallengeAsync(options, progress, cancellationToken);
            if (!challenge.Succeeded) return Failed(StoryWireStage.ChallengeState);
            if (challenge.UnavailableUntilUtc is not null)
                return new StoryWireTestResult(true, StoryWireStage.ChallengeState, challenge.Status,
                    challenge.UnavailableUntilUtc, challenge.DailyLimitReached);
            options = options with { Map = challenge.Map ?? options.Map };
            if (!await CheckAsync(StoryWireStage.MatchPreview, DebugWorkflowCatalog.MatchPreview,
                    _debug.CheckMatchPreviewAsync, options, progress, cancellationToken))
                return Failed(StoryWireStage.MatchPreview);
        }
        else if (options.GameMode == WireGameMode.Expedition)
        {
            if (!await TransitionAsync(
                    StoryWireStage.StoryMap,
                    DebugWorkflowCatalog.PlayUi,
                    DebugWorkflowCatalog.ExpeditionMap,
                    token => _debug.SelectModeAsync("Expedition", options.Device, token),
                    options, progress, cancellationToken) ||
                !await TransitionAsync(
                    StoryWireStage.MatchPreview,
                    DebugWorkflowCatalog.ExpeditionMap,
                    DebugWorkflowCatalog.MatchPreview,
                    token => _debug.SelectExpeditionMapAsync(options.Map, options.ExpeditionDifficulty, options.Device, token),
                    options, progress, cancellationToken))
                return Failed(StoryWireStage.MatchPreview);
        }
        else if (options.GameMode is WireGameMode.Story or WireGameMode.Raid)
        {
            DebugStateSpec modeMap = options.GameMode == WireGameMode.Story
                ? DebugWorkflowCatalog.StoryMap
                : DebugWorkflowCatalog.RaidMap;
            if (!await TransitionAsync(
                    StoryWireStage.StoryMap,
                    DebugWorkflowCatalog.PlayUi,
                    modeMap,
                    token => _debug.SelectModeAsync(options.GameMode.ToString(), options.Device, token),
                    options, progress, cancellationToken))
                return Failed(StoryWireStage.StoryMap);
            if (!await SelectAndCheckActAsync(options, progress, cancellationToken))
                return Failed(StoryWireStage.StoryAct);
            DebugStateSpec actPicker = options.GameMode == WireGameMode.Story
                ? DebugWorkflowCatalog.StoryActPicker
                : DebugWorkflowCatalog.RaidActPicker;
            if (!await TransitionAsync(
                    StoryWireStage.MatchPreview,
                    actPicker,
                    DebugWorkflowCatalog.MatchPreview,
                    token => SelectActAsync(options, token),
                    options, progress, cancellationToken))
                return Failed(StoryWireStage.MatchPreview);
        }
        if (options.GameMode == WireGameMode.Expedition)
        {
            if (!await ActAsync(
                    StoryWireStage.MatchPrestart,
                    token => _debug.StartMatchAsync(options.Device, token),
                    progress,
                    cancellationToken))
                return Failed(StoryWireStage.MatchPrestart);
        }
        else if (!await TransitionAsync(
                     StoryWireStage.MatchPrestart,
                     DebugWorkflowCatalog.MatchPreview,
                     DebugWorkflowCatalog.MatchPrestart,
                     token => _debug.StartMatchAsync(options.Device, token),
                     options,
                     progress,
                     cancellationToken))
        {
            return Failed(StoryWireStage.MatchPrestart);
        }

        if (!options.RunMatchRuntime)
            return new StoryWireTestResult(true, StoryWireStage.MatchPrestart, "MATCH PRESTART VERIFIED");

        if (options.GameMode == WireGameMode.Tower)
        {
            StoryWireTestOptions? loaded = await _towerTeam.LoadAsync(
                options, progress, cancellationToken).ConfigureAwait(false);
            if (loaded is null)
                return Failed(StoryWireStage.LoadTeam);
            options = loaded;
        }

        StoryWireTestResult result = options.GameMode == WireGameMode.Expedition
            ? await _expeditionRuntime.RunAsync(options, progress, alignCamera: true, cancellationToken)
            : await _matchRuntime.RunAsync(options, progress, alignCamera: true, cancellationToken);
        return options.GameMode == WireGameMode.Tower && result.Succeeded
            ? result with
            {
                ResolvedMap = options.Map,
                ResolvedTeam = options.TeamNumber,
                TowerFloor = options.TowerFloor,
            }
            : result;
    }

    public async Task<StoryWireTestResult> RunRepeatedAsync(
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(progress);
        if (!WireGameModeRepeatPolicy.Supports(options.GameMode) && options.GameMode != WireGameMode.Tower)
            throw new InvalidOperationException($"{options.GameMode} cannot continue through Repeat Stage.");

        await _debug.PrepareAsync(cancellationToken);
        if (options.GameMode == WireGameMode.Expedition)
        {
            return await _expeditionRuntime.RunAsync(
                options, progress, alignCamera: false, cancellationToken);
        }
        if (!await CheckAsync(
                StoryWireStage.MatchPrestart,
                DebugWorkflowCatalog.MatchPrestart,
                _debug.CheckMatchPrestartAsync,
                options,
                progress,
                cancellationToken))
            return Failed(StoryWireStage.MatchPrestart);

        if (!options.RunMatchRuntime)
            return new StoryWireTestResult(true, StoryWireStage.MatchPrestart, "MATCH PRESTART VERIFIED");

        return await _matchRuntime.RunAsync(options, progress, alignCamera: false, cancellationToken);
    }

    public Task RepeatStageAsync(
        MatchTerminalOutcome outcome,
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken) =>
        _matchRuntime.RepeatStageAsync(outcome, options, progress, cancellationToken);

    public Task RepeatTowerFloorAsync(
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken) =>
        _matchRuntime.RepeatTowerFloorAsync(options, progress, cancellationToken);

    private async Task<ChallengeNavigationResult> NavigateChallengeAsync(
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        if (!await TransitionAsync(
                StoryWireStage.ChallengeType,
                DebugWorkflowCatalog.PlayUi,
                DebugWorkflowCatalog.ChallengeTypePicker,
                token => _debug.SelectModeAsync("Challenge", options.Device, token),
                options, progress, cancellationToken))
            return new ChallengeNavigationResult(false, "CHALLENGE TYPE BLOCKED", null, null, null, false);
        return await _challenge.NavigateAsync(options, progress, cancellationToken);
    }

    private async Task<bool> SelectAndCheckActAsync(
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task<DebugRunReport>> select = options.GameMode == WireGameMode.Story
            ? token => _debug.SelectMapAsync(options.Map, options.Device, token)
            : token => _debug.SelectRaidMapAsync(options.Map, options.Device, token);
        DebugStateSpec source = options.GameMode == WireGameMode.Story
            ? DebugWorkflowCatalog.StoryMap
            : DebugWorkflowCatalog.RaidMap;
        DebugStateSpec destination = options.GameMode == WireGameMode.Story
            ? DebugWorkflowCatalog.StoryActPicker
            : DebugWorkflowCatalog.RaidActPicker;
        return await TransitionAsync(
            StoryWireStage.StoryAct,
            source,
            destination,
            select,
            options,
            progress,
            cancellationToken);
    }

    private Task<DebugRunReport> SelectActAsync(StoryWireTestOptions options, CancellationToken cancellationToken) =>
        options.GameMode == WireGameMode.Story
            ? _debug.SelectActAsync(options.Act, options.Difficulty, options.Device, cancellationToken)
            : _debug.SelectRaidActAsync(options.Act, options.Device, cancellationToken);

    private async Task<bool> LoadTeamAsync(
        StoryWireTestOptions options,
        DebugStateSpec returnState,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        if (!await ActAsync(
                StoryWireStage.LoadTeam,
                token => _debug.LoadTeamAsync(options.TeamNumber, options.Device, token),
                progress,
                cancellationToken)) return false;
        if (!await CheckAsync(
                StoryWireStage.LoadTeam,
                DebugWorkflowCatalog.TeamSwap,
                _debug.CheckTeamSwapAsync,
                options,
                progress,
                cancellationToken)) return false;

        return options.NavigationKeys.UnitInventory is int unitInventoryKey
            ? await TransitionAsync(
                StoryWireStage.LoadTeam,
                DebugWorkflowCatalog.TeamSwap,
                returnState,
                token => PressNavigationKeyAsync(
                    StoryWireStage.LoadTeam,
                    unitInventoryKey,
                    options.PlacementKeys.ReservedVirtualKey,
                    progress,
                    token),
                options,
                progress,
                cancellationToken)
            : await TransitionAsync(
                StoryWireStage.LoadTeam,
                DebugWorkflowCatalog.TeamSwap,
                returnState,
                token => _lobby.CloseUnitsViaButtonAsync(options.Device, token),
                options,
                progress,
                cancellationToken);
    }

    private async Task<bool> OpenNavigationAsync(
        StoryWireStage stage,
        int? virtualKey,
        Func<CancellationToken, Task<DebugRunReport>> clickFallback,
        DebugStateSpec source,
        DebugStateSpec destination,
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task<ObservedStateTransitionActionResult>> action = virtualKey is int key
            ? token => PressNavigationKeyAsync(
                stage, key, options.PlacementKeys.ReservedVirtualKey, progress, token)
            : async token => ObservedStateTransitionActionResult.From(await clickFallback(token));
        return await TransitionAsync(
            stage, source, destination, action, options, progress, cancellationToken);
    }

    private async Task<ObservedStateTransitionActionResult> PressNavigationKeyAsync(
        StoryWireStage stage,
        int virtualKey,
        int reservedVirtualKey,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        bool succeeded = await ActKeyAsync(
            stage, virtualKey, reservedVirtualKey, progress, cancellationToken);
        string keyName = KeyboardKey.GetDisplayName(virtualKey).ToUpperInvariant();
        return new ObservedStateTransitionActionResult(
            succeeded,
            succeeded ? $"KEY {keyName} SENT" : $"KEY {keyName} BLOCKED",
            [$"KEY {keyName}"]);
    }

    private Task<bool> TransitionAsync(
        StoryWireStage stage,
        DebugStateSpec source,
        DebugStateSpec destination,
        Func<CancellationToken, Task<DebugRunReport>> sourceAction,
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken) =>
        _transitions.RunAsync(
            stage,
            source,
            destination,
            options.Device,
            sourceAction,
            progress,
            cancellationToken);

    private Task<bool> TransitionAsync(
        StoryWireStage stage,
        DebugStateSpec source,
        DebugStateSpec destination,
        Func<CancellationToken, Task<ObservedStateTransitionActionResult>> sourceAction,
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken) =>
        _transitions.RunAsync(
            stage,
            source,
            destination,
            options.Device,
            sourceAction,
            progress,
            cancellationToken);

    private async Task<bool> ActKeyAsync(
        StoryWireStage stage,
        int virtualKey,
        int reservedVirtualKey,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        string keyName = KeyboardKey.GetDisplayName(virtualKey).ToUpperInvariant();
        progress.Report(new StoryWireProgress(stage, StoryWireStageStatus.Running, $"KEY {keyName}", []));
        deepDebug.RecordEvent("wire", "navigation_key_started", new { Stage = Format(stage), Key = keyName });
        AutomationKeyPress press = AutomationKeyPress.Create(virtualKey, 80, reservedVirtualKey);
        await workspace.RunKeySequenceAsync(
            DebugWorkflowCatalog.ClientSize,
            AutomationKeySequence.Create([press]),
            cancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        deepDebug.RecordEvent("wire", "navigation_key_completed", new { Stage = Format(stage), Key = keyName });
        return true;
    }

    private Task<bool> CheckAsync(
        StoryWireStage stage,
        DebugStateSpec state,
        Func<string, CancellationToken, Task<DebugRunReport>> check,
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken) =>
        _state.WaitAsync(stage, state, token => check(options.Device, token), options.Mode, progress, cancellationToken);

    private Task<bool> ActAsync(
        StoryWireStage stage,
        Func<CancellationToken, Task<DebugRunReport>> action,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken) =>
        _state.ActAsync(stage, action, progress, cancellationToken);

    private static StoryWireTestResult Failed(StoryWireStage stage) =>
        new(false, stage, $"{Format(stage)} BLOCKED");

    internal static string Format(StoryWireStage stage) => stage switch
    {
        StoryWireStage.LoadTeam => "LOAD TEAM",
        StoryWireStage.StoryMap => "MAP SELECT",
        StoryWireStage.StoryAct => "ACT SELECT",
        StoryWireStage.ChallengeType => "CHALLENGE TYPE",
        StoryWireStage.ChallengeState => "CHALLENGE STATE",
        StoryWireStage.MatchPreview => "MATCH PREVIEW",
        StoryWireStage.MatchPrestart => "MATCH PRESTART",
        StoryWireStage.MatchRuntime => "MATCH RUNTIME",
        StoryWireStage.TowerType => "TOWER TYPE",
        StoryWireStage.TowerFloor => "TOWER FLOOR",
        StoryWireStage.TowerStage => "TOWER STAGE",
        _ => stage.ToString().ToUpperInvariant(),
    };

}
