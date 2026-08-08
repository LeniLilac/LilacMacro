using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;
using static LilacMacro.App.Debugging.DebugReportFactory;

namespace LilacMacro.App.Debugging;

internal sealed class DebugOcrController(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);
    private readonly DebugExpeditionMapRunner _expeditionMaps = new(workspace, ocr);
    private readonly DebugLobbyRunner _lobby = new(workspace, ocr);
    private readonly DebugResultRunner _results = new(workspace, ocr);
    private readonly DebugTeamSwapRunner _teamSwap = new(workspace, ocr);
    private readonly DebugUnitInventoryRunner _unitInventory = new(workspace, ocr);

    public Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        _states.EnsureAvailable();
        return workspace.ApplyClientSizeAsync(DebugWorkflowCatalog.ClientSize, cancellationToken);
    }

    public Task<DebugRunReport> CheckLobbyAsync(
        string device,
        CancellationToken cancellationToken = default) =>
        _lobby.CheckLobbyAsync(device, cancellationToken);

    public Task<DebugRunReport> OpenPlayAsync(
        string device,
        CancellationToken cancellationToken = default) =>
        _lobby.OpenPlayAsync(device, cancellationToken);

    public Task<DebugRunReport> OpenEventsAsync(
        string device,
        CancellationToken cancellationToken = default) =>
        _lobby.OpenEventsAsync(device, cancellationToken);

    public Task<DebugRunReport> CheckEventsAsync(
        string device,
        CancellationToken cancellationToken = default) =>
        _lobby.CheckEventsAsync(device, cancellationToken);

    public Task<DebugRunReport> SelectEventAsync(
        EventDestination destination,
        string device,
        CancellationToken cancellationToken = default) =>
        _lobby.SelectEventAsync(destination, device, cancellationToken);

    public Task<DebugRunReport> OpenAreasAsync(
        string device,
        CancellationToken cancellationToken = default) =>
        _lobby.OpenAreasAsync(device, cancellationToken);

    public Task<DebugRunReport> CheckAreasAsync(
        string device,
        CancellationToken cancellationToken = default) =>
        _lobby.CheckAreasAsync(device, cancellationToken);

    public Task<DebugRunReport> SelectAreaAsync(
        AreaCategory category,
        string device,
        CancellationToken cancellationToken = default) =>
        _lobby.SelectAreaAsync(category, device, cancellationToken);

    public Task<DebugRunReport> CheckPlayUiAsync(
        string device,
        CancellationToken cancellationToken = default) =>
        _lobby.CheckPlayUiAsync(device, cancellationToken);

    public Task<DebugRunReport> SelectModeAsync(
        string mode,
        string device,
        CancellationToken cancellationToken = default) =>
        _lobby.SelectModeAsync(mode, device, cancellationToken);

    public Task<DebugRunReport> CheckUnitInventoryAsync(
        string device,
        CancellationToken cancellationToken = default) =>
        _unitInventory.CheckAsync(device, cancellationToken);

    public Task<DebugRunReport> OpenTeamsAsync(
        string device,
        CancellationToken cancellationToken = default) =>
        _unitInventory.OpenTeamsAsync(device, cancellationToken);

    public Task<DebugRunReport> CheckTeamSwapAsync(
        string device,
        CancellationToken cancellationToken = default) =>
        _teamSwap.CheckAsync(device, cancellationToken);

    public Task<DebugRunReport> LoadTeamAsync(
        int teamNumber,
        string device,
        CancellationToken cancellationToken = default) =>
        _teamSwap.LoadAsync(teamNumber, device, cancellationToken);

    public async Task<DebugRunReport> CheckChallengeTypesAsync(
        string device,
        CancellationToken cancellationToken = default)
    {
        DebugOcrSnapshot snapshot = await _states.RunAsync(
            DebugWorkflowCatalog.ChallengeTypePicker,
            device,
            cancellationToken);
        if (!snapshot.Evaluation.IsMatch) return FailedState(snapshot);

        ChallengeTypePickerLayout? layout = CreateChallengeLayout(snapshot);
        return layout is null
            ? MissingChallengeAnchors(snapshot)
            : new DebugRunReport(
                snapshot,
                true,
                "CHALLENGE TYPE TRUE",
                [StateLine(snapshot), ChallengeLayoutLine(layout)]);
    }

    public async Task<DebugRunReport> SelectChallengeTypeAsync(
        RegularChallengeType type,
        string device,
        CancellationToken cancellationToken = default)
    {
        DebugOcrSnapshot snapshot = await _states.RunAsync(
            DebugWorkflowCatalog.ChallengeTypePicker,
            device,
            cancellationToken);
        if (!snapshot.Evaluation.IsMatch) return FailedState(snapshot);

        ChallengeTypePickerLayout? layout = CreateChallengeLayout(snapshot);
        if (layout is null) return MissingChallengeAnchors(snapshot);
        PixelPoint point = layout.GetTypePoint(type);
        await workspace.ClickRobloxAsync(DebugWorkflowCatalog.ClientSize, point, cancellationToken);
        return DerivedClickReport(
            snapshot,
            type.ToString().ToUpperInvariant(),
            point,
            ChallengeLayoutLine(layout));
    }

    public async Task<DebugRunReport> CheckMapsAsync(
        string device,
        CancellationToken cancellationToken = default)
    {
        DebugOcrSnapshot snapshot = await _states.RunAsync(DebugWorkflowCatalog.StoryMap, device, cancellationToken);
        return StateReport(snapshot);
    }

    public async Task<DebugRunReport> SelectMapAsync(
        string map,
        string device,
        CancellationToken cancellationToken = default)
    {
        bool scrolled = false;
        if (map is "Fairy King Forest" or "King's Tomb")
        {
            await PrepareAsync(cancellationToken);
            await workspace.ScrollRobloxAsync(
                DebugWorkflowCatalog.ClientSize,
                new PixelPoint(
                    DebugWorkflowCatalog.ClientSize.Width / 2,
                    DebugWorkflowCatalog.ClientSize.Height / 2),
                -2000,
                TimeSpan.FromSeconds(2),
                cancellationToken);
            await Task.Delay(250, cancellationToken);
            scrolled = true;
        }

        DebugOcrSnapshot snapshot = await _states.RunAsync(DebugWorkflowCatalog.StoryMap, device, cancellationToken);
        DebugRunReport report;
        if (!snapshot.Evaluation.IsMatch)
        {
            report = FailedState(snapshot);
        }
        else if (DebugWorkflowCatalog.MapTargets.FirstOrDefault(
                     rule => rule.Name.Equals(map, StringComparison.Ordinal)) is not { } rule ||
                 OcrRuleEngine.FindLeftmostTarget(rule, snapshot.Regions) is not { } target)
        {
            report = MissingTarget(snapshot, map.ToUpperInvariant());
        }
        else
        {
            PixelPoint point = target.Region.Bounds.TopCenter;
            await workspace.ClickRobloxAsync(DebugWorkflowCatalog.ClientSize, point, cancellationToken);
            report = ClickReport(snapshot, target, point, "TOP");
        }
        return scrolled
            ? report with { Events = ["SCROLL -2000 / 2000 MS", .. report.Events] }
            : report;
    }

    public async Task<DebugRunReport> CheckRaidMapsAsync(
        string device,
        CancellationToken cancellationToken = default)
    {
        DebugOcrSnapshot snapshot = await _states.RunAsync(
            DebugWorkflowCatalog.RaidMap,
            device,
            cancellationToken);
        return StateReport(snapshot);
    }

    public async Task<DebugRunReport> SelectRaidMapAsync(
        string map,
        string device,
        CancellationToken cancellationToken = default)
    {
        DebugOcrSnapshot snapshot = await _states.RunAsync(
            DebugWorkflowCatalog.RaidMap,
            device,
            cancellationToken);
        if (!snapshot.Evaluation.IsMatch) return FailedState(snapshot);

        OcrTargetMatch? target = Find(snapshot, map);
        if (target is null) return MissingTarget(snapshot, map.ToUpperInvariant());
        PixelPoint point = target.Region.Bounds.TopCenter;
        await workspace.ClickRobloxAsync(DebugWorkflowCatalog.ClientSize, point, cancellationToken);
        return ClickReport(snapshot, target, point, "TOP");
    }

    public Task<DebugRunReport> CheckExpeditionMapsAsync(
        string device,
        CancellationToken cancellationToken = default) =>
        _expeditionMaps.CheckAsync(device, cancellationToken);

    public Task<DebugRunReport> SelectExpeditionMapAsync(
        string map,
        int difficulty,
        string device,
        CancellationToken cancellationToken = default) =>
        _expeditionMaps.SelectAsync(map, difficulty, device, cancellationToken);

    public Task<DebugRunReport> CheckActsAsync(
        string device,
        CancellationToken cancellationToken = default) =>
        CheckActPickerAsync(
            DebugWorkflowCatalog.StoryActPicker,
            ActPickerKind.Story,
            device,
            cancellationToken);

    public Task<DebugRunReport> SelectActAsync(
        StoryAct act,
        StoryDifficulty difficulty,
        string device,
        CancellationToken cancellationToken = default) =>
        SelectActPickerAsync(
            DebugWorkflowCatalog.StoryActPicker,
            ActPickerKind.Story,
            act,
            difficulty,
            device,
            cancellationToken);

    public Task<DebugRunReport> CheckRaidActsAsync(
        string device,
        CancellationToken cancellationToken = default) =>
        CheckActPickerAsync(
            DebugWorkflowCatalog.RaidActPicker,
            ActPickerKind.Raid,
            device,
            cancellationToken);

    public Task<DebugRunReport> SelectRaidActAsync(
        StoryAct act,
        string device,
        CancellationToken cancellationToken = default) =>
        SelectActPickerAsync(
            DebugWorkflowCatalog.RaidActPicker,
            ActPickerKind.Raid,
            act,
            StoryDifficulty.Normal,
            device,
            cancellationToken);

    private async Task<DebugRunReport> CheckActPickerAsync(
        DebugStateSpec state,
        ActPickerKind kind,
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot snapshot = await _states.RunAsync(state, device, cancellationToken);
        if (!snapshot.Evaluation.IsMatch) return FailedState(snapshot);

        ActPickerLayout? layout = CreateActLayout(snapshot, kind);
        return layout is null
            ? MissingActAnchors(snapshot, kind)
            : new DebugRunReport(
                snapshot,
                true,
                $"{snapshot.State} TRUE",
                [StateLine(snapshot), LayoutLine(layout)]);
    }

    private async Task<DebugRunReport> SelectActPickerAsync(
        DebugStateSpec state,
        ActPickerKind kind,
        StoryAct act,
        StoryDifficulty difficulty,
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot initial = await _states.RunAsync(state, device, cancellationToken);
        if (!initial.Evaluation.IsMatch) return FailedState(initial);

        ActPickerLayout? layout = CreateActLayout(initial, kind);
        if (layout is null) return MissingActAnchors(initial, kind);
        if (!layout.SupportsAct(act)) return UnsupportedAct(initial, act);

        OcrTargetRule confirmationRule = DebugWorkflowCatalog.ConfirmationFor(act);
        PixelPoint actPoint = layout.GetActPoint(act);
        await workspace.ClickRobloxAsync(DebugWorkflowCatalog.ClientSize, actPoint, cancellationToken);
        await Task.Delay(250, cancellationToken);

        DebugOcrSnapshot confirmation = await _states.RunAsync(state, device, cancellationToken);
        IReadOnlyList<OcrTextRegion> regions = Regions(confirmation);
        OcrTargetMatch? confirmed = OcrRuleEngine.FindTarget(confirmationRule, regions);
        if (confirmed is null)
        {
            return ActClickBlocked(
                confirmation,
                $"{confirmationRule.Name.ToUpperInvariant()} NOT CONFIRMED",
                initial,
                layout,
                confirmationRule.Name,
                actPoint);
        }

        OcrTextRegion? selectStage = ActPickerLayout.FindSelectStage(regions);
        if (selectStage is null)
        {
            return ActClickBlocked(
                confirmation,
                "SELECT STAGE NOT FOUND",
                initial,
                layout,
                confirmationRule.Name,
                actPoint);
        }

        PixelPoint? difficultyPoint = null;
        if (layout.SupportsDifficulty && UsesDifficulty(act))
        {
            ActPickerLayout? confirmedLayout = CreateActLayout(confirmation, kind);
            if (confirmedLayout is null)
            {
                return ActClickBlocked(
                    confirmation,
                    "DIFFICULTY ANCHORS MISSING",
                    initial,
                    layout,
                    confirmationRule.Name,
                    actPoint);
            }
            difficultyPoint = confirmedLayout.GetDifficultyPoint(difficulty);
            await workspace.ClickRobloxAsync(
                DebugWorkflowCatalog.ClientSize,
                difficultyPoint.Value,
                cancellationToken);
        }

        PixelPoint selectPoint = selectStage.Bounds.Center;
        await workspace.ClickRobloxAsync(DebugWorkflowCatalog.ClientSize, selectPoint, cancellationToken);
        List<string> events =
        [
            StateLine(initial),
            LayoutLine(layout),
            $"{confirmationRule.Name.ToUpperInvariant()} [{actPoint.X},{actPoint.Y}] DERIVED",
            "WAIT 250 MS",
            StateLine(confirmation),
            $"CONFIRMED [{confirmed.Region.Bounds.X},{confirmed.Region.Bounds.Y}," +
                $"{confirmed.Region.Bounds.Width},{confirmed.Region.Bounds.Height}]",
        ];
        if (difficultyPoint is { } derivedDifficulty)
        {
            events.Add($"{difficulty.ToString().ToUpperInvariant()} " +
                $"[{derivedDifficulty.X},{derivedDifficulty.Y}] DERIVED");
        }
        events.Add($"SELECT STAGE [{selectPoint.X},{selectPoint.Y}] CENTER");

        string actLabel = confirmationRule.Name.ToUpperInvariant();
        string status = difficultyPoint is null
            ? $"{actLabel} + SELECT CLICKED"
            : $"{actLabel} {difficulty.ToString().ToUpperInvariant()} + SELECT CLICKED";
        return new DebugRunReport(
            confirmation,
            true,
            status,
            events);
    }

    public async Task<DebugRunReport> CheckMatchPreviewAsync(
        string device,
        CancellationToken cancellationToken = default)
    {
        DebugOcrSnapshot snapshot = await _states.RunAsync(
            DebugWorkflowCatalog.MatchPreview,
            device,
            cancellationToken);
        return StateReport(snapshot);
    }

    public async Task<DebugRunReport> StartMatchAsync(
        string device,
        CancellationToken cancellationToken = default)
    {
        DebugOcrSnapshot snapshot = await _states.RunAsync(
            DebugWorkflowCatalog.MatchPreview,
            device,
            cancellationToken);
        if (!snapshot.Evaluation.IsMatch) return FailedState(snapshot);

        OcrTargetMatch? start = Find(snapshot, "Start");
        if (start is null) return MissingTarget(snapshot, "START");
        PixelPoint point = start.Region.Bounds.Center;
        await workspace.ClickRobloxAsync(DebugWorkflowCatalog.ClientSize, point, cancellationToken);
        return ClickReport(snapshot, start, point, "CENTER");
    }

    public async Task<DebugRunReport> CheckMatchPrestartAsync(
        string device,
        CancellationToken cancellationToken = default)
    {
        DebugOcrSnapshot snapshot = await _states.RunAsync(
            DebugWorkflowCatalog.MatchPrestart,
            device,
            cancellationToken);
        return StateReport(snapshot);
    }

    public async Task<DebugRunReport> StartGameAsync(
        string device,
        CancellationToken cancellationToken = default)
    {
        DebugOcrSnapshot snapshot = await _states.RunAsync(
            DebugWorkflowCatalog.MatchPrestart,
            device,
            cancellationToken);
        if (!snapshot.Evaluation.IsMatch) return FailedState(snapshot);

        OcrTargetMatch? startGame = snapshot.Evaluation.Matches
            .OrderByDescending(match => match.Region.Bounds.Center.Y)
            .ThenByDescending(match => match.Region.Bounds.Center.X)
            .FirstOrDefault();
        if (startGame is null) return MissingTarget(snapshot, "START GAME");
        PixelPoint point = startGame.Region.Bounds.Center;
        await workspace.ClickRobloxAsync(DebugWorkflowCatalog.ClientSize, point, cancellationToken);
        return ClickReport(snapshot, startGame, point, "LOWEST CENTER");
    }

    public Task<DebugRunReport> CheckDefeatAsync(
        string device,
        CancellationToken cancellationToken = default) =>
        _results.CheckAsync(DebugWorkflowCatalog.Defeat, device, cancellationToken);

    public Task<DebugRunReport> RepeatStageAsync(
        string device,
        CancellationToken cancellationToken = default) =>
        _results.RepeatAsync(DebugWorkflowCatalog.Defeat, device, cancellationToken);

    public Task<DebugRunReport> CheckVictoryAsync(
        string device,
        CancellationToken cancellationToken = default) =>
        _results.CheckAsync(DebugWorkflowCatalog.Victory, device, cancellationToken);

    public Task<DebugRunReport> RepeatVictoryStageAsync(
        string device,
        CancellationToken cancellationToken = default) =>
        _results.RepeatAsync(DebugWorkflowCatalog.Victory, device, cancellationToken);

    public Task SetupAsync(string device, CancellationToken cancellationToken = default) =>
        ocr.SetupAsync(device, cancellationToken);

    private static ActPickerLayout? CreateActLayout(
        DebugOcrSnapshot snapshot,
        ActPickerKind kind) =>
        ActPickerLayout.TryCreate(Regions(snapshot), DebugWorkflowCatalog.ClientSize, kind);

    private static ChallengeTypePickerLayout? CreateChallengeLayout(DebugOcrSnapshot snapshot) =>
        ChallengeTypePickerLayout.TryCreate(Regions(snapshot), DebugWorkflowCatalog.ClientSize);

    private static IReadOnlyList<OcrTextRegion> Regions(DebugOcrSnapshot snapshot) =>
        snapshot.Regions;

    private static bool UsesDifficulty(StoryAct act) => act is
        StoryAct.Act1 or
        StoryAct.Act2 or
        StoryAct.Act3 or
        StoryAct.Act4 or
        StoryAct.Act5;

    private static OcrTargetMatch? Find(DebugOcrSnapshot snapshot, string target) =>
        snapshot.Evaluation.Matches.FirstOrDefault(match => match.Target.Equals(target, StringComparison.Ordinal));

}
