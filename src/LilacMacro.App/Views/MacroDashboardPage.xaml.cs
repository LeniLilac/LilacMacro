using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LilacMacro.App.Controls;
using LilacMacro.App.Debugging;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Notifications;
using LilacMacro.App.Runtime;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Ocr;
using LilacMacro.Core.Placements;

namespace LilacMacro.App.Views;

public partial class MacroDashboardPage : UserControl
{
    private readonly DeepDebugSessionService _deepDebug;
    private readonly MacroOwnerState _ownerState;
    private readonly WorkspaceController _workspace;
    private readonly OcrRunner _ocr;
    private readonly StoryWireTestRunner _runner;
    private readonly PrivateServerRejoinService _rejoin;
    private readonly PlacementSetupStore _placements;
    private readonly ChallengePlacementResolver _challengePlacements;
    private readonly Stopwatch _runtime = new();
    private readonly Dictionary<PlanTaskPrototype, int> _victories = [];
    private readonly Dictionary<PlanTaskPrototype, int> _defeats = [];
    private readonly Dictionary<PlanTaskPrototype, DateTimeOffset> _blockedUntil = [];
    private readonly List<RunStatsPoint> _runStats = [];
    private readonly DispatcherTimer _runtimeTimer;
    private DeepDebugScope? _debugScope;
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private bool _runStarting;
    private bool _initialized;
    private PlanTaskPrototype? _currentTask;

    internal MacroDashboardPage(DeepDebugSessionService deepDebug, MacroOwnerState ownerState)
    {
        _deepDebug = deepDebug;
        _ownerState = ownerState;
        _workspace = new WorkspaceController(deepDebug);
        _ocr = new OcrRunner(deepDebug) { KeepLoaded = true };
        _runner = new StoryWireTestRunner(_workspace, _ocr, deepDebug);
        _rejoin = new PrivateServerRejoinService(_workspace, _ocr);
        _placements = new PlacementSetupStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LilacMacro",
            "placements"));
        _challengePlacements = new ChallengePlacementResolver(_placements);
        InitializeComponent();
        _runtimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _runtimeTimer.Tick += (_, _) => RuntimeText.Text = _runtime.Elapsed.ToString(@"hh\:mm\:ss");
        StatsChart.SetPoints(_runStats);
        PlanCombo.DisplayMemberPath = nameof(PlanPrototype.Name);
        PlanCombo.ItemsSource = ownerState.Plans;
        PlanCombo.SelectedIndex = 0;
    }

    public bool SetDashboardActive(bool active, out string error)
    {
        if (!active && _runTask is not null)
        {
            error = "Stop the macro before leaving the Macro tab.";
            return false;
        }
        return RobloxDock.SetDashboardActive(active, out error);
    }

    public bool TryPrepareForClose(out string error)
    {
        _runCancellation?.Cancel();
        return RobloxDock.TryPrepareForClose(out error);
    }

    public async Task CompleteForCloseAsync()
    {
        _runCancellation?.Cancel();
        if (_runTask is not null)
        {
            try { await _runTask; }
            catch (OperationCanceledException) { }
        }
        await CompleteDebugAsync("closed");
        _ocr.Dispose();
        _workspace.Dispose();
    }

    private void PlanCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (PlanCombo.SelectedItem is not PlanPrototype plan || UpcomingTasksList is null) return;
        _currentTask = null;
        RefreshUpcomingTasks(plan);
    }

    private async void StartButton_OnClick(object sender, RoutedEventArgs eventArgs) => await StartMacroAsync();

    internal void ToggleRunFromHotkey()
    {
        if (_runTask is not null || _runStarting)
        {
            _runCancellation?.Cancel();
            return;
        }
        _ = StartMacroAsync();
    }

    private async Task StartMacroAsync()
    {
        if (_runTask is not null || _runStarting || PlanCombo.SelectedItem is not PlanPrototype plan) return;
        _runStarting = true;
        try
        {
            PrivateServerRejoinService.Validate(_ownerState.PrivateServerLink);
            string device = SelectOcrDevice();
            if (!_initialized)
            {
                await _workspace.InitializeAsync();
                _initialized = true;
            }
            _runCancellation = new CancellationTokenSource();
            _debugScope = await _deepDebug.OpenSessionAsync(
                "macro-runtime",
                new DeepDebugOperationContext("main-macro", new { Plan = plan.Name }));
            _runtime.Restart();
            _runtimeTimer.Start();
            _runStats.Clear();
            StatsChart.SetPoints(_runStats);
            RefreshRunState(true);
            _runTask = RunPlanAsync(plan, device, _runCancellation.Token);
            await _runTask;
            AppendLog("PLAN COMPLETE");
        }
        catch (OperationCanceledException)
        {
            AppendLog("STOPPED");
        }
        catch (Exception error)
        {
            AppToastService.ShowError("MACRO STOPPED", error.Message);
            AppendLog($"ERROR {error.Message}");
            _deepDebug.RecordEvent("macro", "runtime_error", new { Error = error.ToString() });
        }
        finally
        {
            _runStarting = false;
            _runtime.Stop();
            _runtimeTimer.Stop();
            _runTask = null;
            _runCancellation?.Dispose();
            _runCancellation = null;
            await CompleteDebugAsync("stopped");
            RefreshRunState(false);
            _currentTask = null;
            if (PlanCombo.SelectedItem is PlanPrototype selectedPlan) RefreshUpcomingTasks(selectedPlan);
        }
    }

    private async Task RunPlanAsync(PlanPrototype plan, string device, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            PlanTaskPrototype? task = MacroPriorityPolicy.Select(
                plan,
                _victories,
                candidate => !_blockedUntil.TryGetValue(candidate, out DateTimeOffset until) || now >= until);
            if (task is null)
            {
                PlanTaskPrototype[] pending = MacroPriorityPolicy.Flatten(plan)
                    .Where(candidate => MacroPriorityPolicy.IsPending(candidate, _victories))
                    .ToArray();
                if (pending.Length == 0) return;
                DateTimeOffset next = pending
                    .Select(candidate => _blockedUntil.GetValueOrDefault(candidate, now))
                    .Where(candidate => candidate > now)
                    .DefaultIfEmpty(now.AddSeconds(5))
                    .Min();
                AppendLog($"WAIT UNTIL {next:yyyy-MM-dd HH:mm:ss}Z");
                await Task.Delay(next - now, cancellationToken);
                continue;
            }
            if (!MacroPriorityPolicy.Supported(task))
                throw new InvalidOperationException($"{task.ModeLabel} runtime is not implemented; priority evaluation stopped.");

            _currentTask = task;
            RefreshUpcomingTasks(plan);
            AppendLog($"RUN {task.Name}");

            StoryWireTestOptions options = await CreateOptionsAsync(task, device, cancellationToken);
            StoryWireTestResult result = await _runner.RunAsync(
                options,
                new Progress<StoryWireProgress>(progress =>
                {
                    AppendLog($"{StoryWireTestRunner.Format(progress.Stage)} | {progress.Detail}");
                    RuntimeText.Text = _runtime.Elapsed.ToString(@"hh\:mm\:ss");
                }),
                cancellationToken);
            if (!result.Succeeded) throw new InvalidOperationException(result.Status);

            if (result.UnavailableUntilUtc is DateTimeOffset unavailableUntil)
            {
                _blockedUntil[task] = unavailableUntil;
                AppendLog(result.Status);
                RefreshUpcomingTasks(plan);
                await _rejoin.RejoinAndVerifyLobbyAsync(
                    _ownerState.PrivateServerLink,
                    device,
                    AppendLog,
                    cancellationToken);
                continue;
            }

            bool victory = result.Status.StartsWith("VICTORY", StringComparison.Ordinal);
            if (victory)
            {
                _victories[task] = _victories.GetValueOrDefault(task) + 1;
            }
            else
            {
                _defeats[task] = _defeats.GetValueOrDefault(task) + 1;
                if (_defeats[task] > task.DefeatRetries)
                    throw new InvalidOperationException($"{task.Name} exceeded its defeat retry limit.");
            }
            _runStats.Add(new RunStatsPoint(_runtime.Elapsed, victory));
            StatsChart.SetPoints(_runStats);
            RefreshUpcomingTasks(plan);

            await _rejoin.RejoinAndVerifyLobbyAsync(
                _ownerState.PrivateServerLink,
                device,
                AppendLog,
                cancellationToken);
        }
    }

    private async Task<StoryWireTestOptions> CreateOptionsAsync(
        PlanTaskPrototype task,
        string device,
        CancellationToken cancellationToken)
    {
        WireGameMode gameMode = task.Mode switch
        {
            PlanTaskMode.Raid => WireGameMode.Raid,
            PlanTaskMode.Challenge => WireGameMode.Challenge,
            _ => WireGameMode.Story,
        };
        (string mapName, StoryAct act) = gameMode == WireGameMode.Challenge
            ? ("AUTO", StoryAct.Act1)
            : ParseRoute(task.Route);
        int team;
        if (gameMode == WireGameMode.Challenge)
        {
            team = await _challengePlacements.ResolveCommonTeamAsync(cancellationToken);
        }
        else
        {
            string mapId = gameMode == WireGameMode.Raid
                ? $"raid-spirit-city-{RouteId(act)}"
                : $"story-{Slug(mapName)}";
            PlacementMapDefinition map = PlacementMapCatalog.Definitions.First(candidate => candidate.Id == mapId);
            PlacementSetupDocument document = await _placements.LoadAsync(map.Id, cancellationToken);
            PlacementRouteDefinition definition = PlacementRouteCatalog.For(map)
                .FirstOrDefault(candidate => candidate.Id == RouteId(act))
                ?? PlacementRouteCatalog.For(map).First(candidate => candidate.IsShared);
            PlacementRouteSetup route = PlacementRouteCatalog.EffectiveRoute(document, definition);
            team = route.TeamSlot;
        }
        MacroRuntimeKeySnapshot keys = _ownerState.KeyBindings.Snapshot();
        RegularChallengeType[] challengeTypes = gameMode == WireGameMode.Challenge
            ? EnabledChallengeTypes(task)
            : [];
        return new StoryWireTestOptions(
            DebugEvidenceMode.ImageWithOcrFallback,
            gameMode,
            team,
            mapName,
            act,
            task.HardMode ? StoryDifficulty.Hard : StoryDifficulty.Normal,
            challengeTypes,
            new StoryWireNavigationKeys(keys.PlayMenu, keys.UnitInventory, keys.AreasMenu),
            keys.Placement,
            keys.ShiftLock,
            device,
            RunMatchRuntime: true,
            RepeatStage: false);
    }

    private static RegularChallengeType[] EnabledChallengeTypes(PlanTaskPrototype task)
    {
        List<RegularChallengeType> types = [];
        if (task.RunTrait) types.Add(RegularChallengeType.Trait);
        if (task.RunStat) types.Add(RegularChallengeType.Stat);
        if (task.RunSprite) types.Add(RegularChallengeType.Sprite);
        if (types.Count == 0) throw new InvalidDataException("Challenge task has no enabled types.");
        return [.. types];
    }

    private string SelectOcrDevice()
    {
        if (_ocr.IsDeviceReady(OcrRunner.GpuDevice)) return OcrRunner.GpuDevice;
        if (_ocr.IsDeviceReady(OcrRunner.CpuDevice)) return OcrRunner.CpuDevice;
        throw new InvalidOperationException("Set up OCR in Dataset Builder before starting the macro.");
    }

    private static (string Map, StoryAct Act) ParseRoute(string route)
    {
        string[] parts = route.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim().TrimEnd('Â').Trim())
            .ToArray();
        string map = parts.FirstOrDefault() ?? throw new InvalidDataException("Task route has no map.");
        string actText = parts.FirstOrDefault(part => part.StartsWith("Act ", StringComparison.OrdinalIgnoreCase))
            ?? parts.FirstOrDefault(part => part.Equals("Infinite", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Mastery", StringComparison.OrdinalIgnoreCase))
            ?? "Act 1";
        StoryAct act = actText.ToLowerInvariant() switch
        {
            "act 1" => StoryAct.Act1,
            "act 2" => StoryAct.Act2,
            "act 3" => StoryAct.Act3,
            "act 4" => StoryAct.Act4,
            "act 5" => StoryAct.Act5,
            "infinite" => StoryAct.Infinite,
            "mastery" => StoryAct.Mastery,
            _ => throw new InvalidDataException("Task route act is invalid."),
        };
        return (map, act);
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

    private static string Slug(string value) => value.ToLowerInvariant().Replace("'", string.Empty).Replace(' ', '-');

    private void StopButton_OnClick(object sender, RoutedEventArgs eventArgs) => _runCancellation?.Cancel();

    private void DockButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        RobloxDock.SetRequested(!RobloxDock.IsRequested);
        RefreshDockState();
    }

    private void RobloxDock_OnStateChanged(object? sender, EventArgs eventArgs) => RefreshDockState();

    private void RefreshDockState()
    {
        if (DockStatusText is null || DockButtonText is null) return;
        DockStatusText.Text = RobloxDock.Status;
        DockButtonText.Text = RobloxDock.IsRequested ? "UNDOCK" : "DOCK";
        DockButton.SetResourceReference(
            Control.BackgroundProperty,
            RobloxDock.IsRequested ? "AccentBrush" : "CardBrush");
    }

    private void RefreshRunState(bool running)
    {
        StartButton.IsEnabled = !running;
        StopButton.IsEnabled = running;
        PlanCombo.IsEnabled = !running;
        RuntimeText.Text = _runtime.Elapsed.ToString(@"hh\:mm\:ss");
    }

    private void RefreshUpcomingTasks(PlanPrototype plan)
    {
        IReadOnlyList<PlanTaskPrototype> tasks = MacroPriorityPolicy.Flatten(plan);
        List<UpcomingTaskRow> rows = tasks
            .Where(task => MacroPriorityPolicy.IsPending(task, _victories))
            .Select((task, index) => new UpcomingTaskRow(
                index + 1,
                task.Name,
                ReferenceEquals(task, _currentTask)
                    ? $"CURRENT · PRIORITY {task.Priority}"
                    : $"{task.ModeLabel.ToUpperInvariant()} · PRIORITY {task.Priority}",
                task.Mode == PlanTaskMode.Challenge && _blockedUntil.TryGetValue(task, out DateTimeOffset until)
                    ? $"NEXT {until:MM-dd HH:mm}Z"
                    : task.Mode is PlanTaskMode.Utilities or PlanTaskMode.Challenge
                    ? task.TargetLabel
                    : $"{_victories.GetValueOrDefault(task)}/{task.Target} W"))
            .ToList();
        UpcomingTasksList.ItemsSource = rows;
    }

    private void AppendLog(string message)
    {
        string entry = $"{DateTime.Now:HH:mm:ss} {message}";
        TraceLogText.Text = string.IsNullOrWhiteSpace(TraceLogText.Text) || TraceLogText.Text == "Macro runtime is not connected."
            ? entry
            : TraceLogText.Text + Environment.NewLine + entry;
        TraceLogText.ScrollToEnd();
    }

    private async Task CompleteDebugAsync(string outcome)
    {
        if (_debugScope is null) return;
        await _debugScope.CompleteAsync(outcome);
        _debugScope = null;
    }
}

internal sealed record UpcomingTaskRow(int Position, string Name, string Detail, string Progress);
