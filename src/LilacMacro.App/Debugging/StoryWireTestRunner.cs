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
    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(400);
    private readonly DebugOcrController _debug = new(workspace, ocr);
    private readonly DebugLobbyRunner _lobby = new(workspace, ocr);
    private readonly ChallengeWireNavigator _challenge = new(workspace, ocr, deepDebug);
    private readonly WireHybridEvidenceService _hybrid = new(workspace, deepDebug);
    private readonly StoryMatchRuntimeRunner _matchRuntime = new(workspace, ocr);

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
        if (!await OpenNavigationAsync(
                StoryWireStage.Units,
                options.NavigationKeys.UnitInventory,
                token => _lobby.OpenUnitsAsync(options.Device, token),
                DebugWorkflowCatalog.UnitInventory,
                _debug.CheckUnitInventoryAsync,
                options,
                progress,
                cancellationToken))
            return Failed(StoryWireStage.Units);
        if (!await ActAsync(StoryWireStage.Teams, token => _debug.OpenTeamsAsync(options.Device, token), progress, cancellationToken) ||
            !await CheckAsync(StoryWireStage.Teams, DebugWorkflowCatalog.TeamSwap, _debug.CheckTeamSwapAsync, options, progress, cancellationToken))
            return Failed(StoryWireStage.Teams);
        if (!await LoadTeamAsync(options, progress, cancellationToken))
            return Failed(StoryWireStage.LoadTeam);
        if (!await OpenNavigationAsync(
                StoryWireStage.Play,
                options.NavigationKeys.PlayMenu,
                token => _debug.OpenPlayAsync(options.Device, token),
                DebugWorkflowCatalog.PlayUi,
                _debug.CheckPlayUiAsync,
                options,
                progress,
                cancellationToken))
            return Failed(StoryWireStage.Play);
        if (options.GameMode == WireGameMode.Challenge)
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
        else
        {
            if (!await ActAsync(StoryWireStage.StoryMap,
                    token => _debug.SelectModeAsync(options.GameMode.ToString(), options.Device, token), progress, cancellationToken) ||
                !await CheckModeMapAsync(options, progress, cancellationToken))
                return Failed(StoryWireStage.StoryMap);
            if (!await SelectAndCheckActAsync(options, progress, cancellationToken))
                return Failed(StoryWireStage.StoryAct);
            if (!await ActAsync(StoryWireStage.MatchPreview, token => SelectActAsync(options, token), progress, cancellationToken) ||
                !await CheckAsync(StoryWireStage.MatchPreview, DebugWorkflowCatalog.MatchPreview,
                    _debug.CheckMatchPreviewAsync, options, progress, cancellationToken))
                return Failed(StoryWireStage.MatchPreview);
        }
        if (!await ActAsync(StoryWireStage.MatchPrestart, token => _debug.StartMatchAsync(options.Device, token), progress, cancellationToken) ||
            !await CheckAsync(StoryWireStage.MatchPrestart, DebugWorkflowCatalog.MatchPrestart, _debug.CheckMatchPrestartAsync, options, progress, cancellationToken))
            return Failed(StoryWireStage.MatchPrestart);

        if (!options.RunMatchRuntime)
            return new StoryWireTestResult(true, StoryWireStage.MatchPrestart, "MATCH PRESTART VERIFIED");

        return await _matchRuntime.RunAsync(options, progress, alignCamera: true, cancellationToken);
    }

    public async Task<StoryWireTestResult> RunRepeatedAsync(
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(progress);
        if (options.GameMode == WireGameMode.Challenge)
            throw new InvalidOperationException("Challenge cannot continue through Repeat Stage.");

        await _debug.PrepareAsync(cancellationToken);
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

    private async Task<ChallengeNavigationResult> NavigateChallengeAsync(
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        if (!await ActAsync(StoryWireStage.ChallengeType,
                token => _debug.SelectModeAsync("Challenge", options.Device, token), progress, cancellationToken) ||
            !await CheckAsync(StoryWireStage.ChallengeType, DebugWorkflowCatalog.ChallengeTypePicker,
                _debug.CheckChallengeTypesAsync, options, progress, cancellationToken))
            return new ChallengeNavigationResult(false, "CHALLENGE TYPE BLOCKED", null, null, null, false);
        return await _challenge.NavigateAsync(options, progress, cancellationToken);
    }

    private Task<bool> CheckModeMapAsync(
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken) => options.GameMode == WireGameMode.Story
        ? CheckAsync(StoryWireStage.StoryMap, DebugWorkflowCatalog.StoryMap, _debug.CheckMapsAsync, options, progress, cancellationToken)
        : CheckAsync(StoryWireStage.StoryMap, DebugWorkflowCatalog.RaidMap, _debug.CheckRaidMapsAsync, options, progress, cancellationToken);

    private async Task<bool> SelectAndCheckActAsync(
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task<DebugRunReport>> select = options.GameMode == WireGameMode.Story
            ? token => _debug.SelectMapAsync(options.Map, options.Device, token)
            : token => _debug.SelectRaidMapAsync(options.Map, options.Device, token);
        if (!await ActAsync(StoryWireStage.StoryAct, select, progress, cancellationToken)) return false;
        return options.GameMode == WireGameMode.Story
            ? await CheckAsync(StoryWireStage.StoryAct, DebugWorkflowCatalog.StoryActPicker, _debug.CheckActsAsync, options, progress, cancellationToken)
            : await CheckAsync(StoryWireStage.StoryAct, DebugWorkflowCatalog.RaidActPicker, _debug.CheckRaidActsAsync, options, progress, cancellationToken);
    }

    private Task<DebugRunReport> SelectActAsync(StoryWireTestOptions options, CancellationToken cancellationToken) =>
        options.GameMode == WireGameMode.Story
            ? _debug.SelectActAsync(options.Act, options.Difficulty, options.Device, cancellationToken)
            : _debug.SelectRaidActAsync(options.Act, options.Device, cancellationToken);

    private async Task<bool> LoadTeamAsync(
        StoryWireTestOptions options,
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

        if (options.NavigationKeys.UnitInventory is int unitInventoryKey)
        {
            if (!await ActKeyAsync(
                    StoryWireStage.LoadTeam,
                    unitInventoryKey,
                    options.PlacementKeys.ReservedVirtualKey,
                    progress,
                    cancellationToken))
                return false;
        }
        else if (!await ActAsync(
                     StoryWireStage.LoadTeam,
                     token => _lobby.CloseUnitsViaButtonAsync(options.Device, token),
                     progress,
                     cancellationToken))
        {
            return false;
        }
        return await CheckAsync(
            StoryWireStage.LoadTeam,
            DebugWorkflowCatalog.Lobby,
            _debug.CheckLobbyAsync,
            options,
            progress,
            cancellationToken);
    }

    private async Task<bool> OpenNavigationAsync(
        StoryWireStage stage,
        int? virtualKey,
        Func<CancellationToken, Task<DebugRunReport>> clickFallback,
        DebugStateSpec destination,
        Func<string, CancellationToken, Task<DebugRunReport>> check,
        StoryWireTestOptions options,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        bool acted = virtualKey is int key
            ? await ActKeyAsync(
                stage,
                key,
                options.PlacementKeys.ReservedVirtualKey,
                progress,
                cancellationToken)
            : await ActAsync(stage, clickFallback, progress, cancellationToken);
        return acted && await CheckAsync(stage, destination, check, options, progress, cancellationToken);
    }

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
        WaitAsync(stage, state, token => check(options.Device, token), options.Mode, progress, cancellationToken);

    private async Task<bool> ActAsync(
        StoryWireStage stage,
        Func<CancellationToken, Task<DebugRunReport>> action,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        progress.Report(new StoryWireProgress(stage, StoryWireStageStatus.Running, "RUNNING", []));
        deepDebug.RecordEvent("wire", "action_started", new { Stage = Format(stage) });
        DebugRunReport report = await action(cancellationToken);
        deepDebug.RecordEvent("wire", "action_completed", new
        {
            Stage = Format(stage),
            report.Succeeded,
            report.Status,
            report.Events,
            Snapshot = WireDebugEvidence.Snapshot(report.Snapshot),
        });
        progress.Report(new StoryWireProgress(
            stage,
            report.Succeeded ? StoryWireStageStatus.Passed : StoryWireStageStatus.Failed,
            report.Status,
            report.Events));
        return report.Succeeded;
    }

    private async Task<bool> WaitAsync(
        StoryWireStage stage,
        DebugStateSpec state,
        Func<CancellationToken, Task<DebugRunReport>> check,
        DebugEvidenceMode mode,
        IProgress<StoryWireProgress> progress,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StateTimeout);
        DebugRunReport? last = null;
        WireImageStateResult? lastImage = null;
        try
        {
            while (true)
            {
                deepDebug.RecordEvent("wire", "state_poll_started", new
                {
                    Stage = Format(stage),
                    State = state.Name,
                    Mode = mode.ToString(),
                });
                if (mode == DebugEvidenceMode.ImageWithOcrFallback)
                {
                    try
                    {
                        lastImage = await _hybrid.TryVerifyAsync(state, timeout.Token);
                        WireDebugEvidence.RecordComparisons(deepDebug, lastImage.Comparisons);
                        deepDebug.RecordEvent("vision", "image_state_evaluated", new
                        {
                            Stage = Format(stage),
                            State = state.Name,
                            lastImage.IsMatch,
                            lastImage.Status,
                            lastImage.Events,
                            lastImage.Comparisons,
                        });
                        if (lastImage.IsMatch)
                        {
                            progress.Report(new StoryWireProgress(
                                stage,
                                StoryWireStageStatus.Passed,
                                lastImage.Status,
                                lastImage.Events,
                                lastImage.Comparisons));
                            return true;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception error)
                    {
                        lastImage = new WireImageStateResult(
                            false,
                            "IMAGE ERROR",
                            [$"IMAGE FALLBACK ERROR {error.Message}"],
                            []);
                    }
                }

                last = await check(timeout.Token);
                deepDebug.RecordEvent("ocr", "state_evaluated", new
                {
                    Stage = Format(stage),
                    last.Succeeded,
                    last.Status,
                    last.Events,
                    Snapshot = WireDebugEvidence.Snapshot(last.Snapshot),
                });
                if (last.Succeeded)
                {
                    IReadOnlyList<WireVisualComparison> comparisons = [];
                    string? imageError = null;
                    if (mode == DebugEvidenceMode.ImageWithOcrFallback)
                    {
                        try
                        {
                            comparisons = await _hybrid.CompareAsync(last, timeout.Token);
                            WireDebugEvidence.RecordComparisons(deepDebug, comparisons);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception error)
                        {
                            imageError = error.Message;
                        }
                    }
                    string detail = mode == DebugEvidenceMode.Ocr
                        ? $"{last.Status} | OCR"
                        : comparisons.Count == 0
                            ? $"{last.Status} | OCR FALLBACK | IMG {(imageError is null ? "0" : "ERROR")}"
                            : $"{last.Status} | OCR FALLBACK | IMG {comparisons.Count(candidate => candidate.Agrees)}/{comparisons.Count}";
                    List<string> events = [.. lastImage?.Events ?? [], .. last.Events];
                    if (mode == DebugEvidenceMode.Ocr)
                    {
                        events.Add("OCR PRIMARY MATCH");
                    }
                    else
                    {
                        events.Add(imageError is null
                            ? $"OCR FALLBACK | IMAGE REFRESH {comparisons.Count(candidate => candidate.Agrees)}/{comparisons.Count} AGREE"
                            : $"OCR FALLBACK | IMAGE ERROR {imageError}");
                    }
                    progress.Report(new StoryWireProgress(
                        stage,
                        StoryWireStageStatus.Passed,
                        detail,
                        events,
                        comparisons));
                    return true;
                }
                await Task.Delay(PollDelay, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            progress.Report(new StoryWireProgress(
                stage,
                StoryWireStageStatus.Failed,
                last?.Status ?? "STATE TIMEOUT",
                [.. lastImage?.Events ?? [], .. last?.Events ?? []]));
            return false;
        }
    }

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
        _ => stage.ToString().ToUpperInvariant(),
    };

}
