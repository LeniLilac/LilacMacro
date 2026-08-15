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
using LilacMacro.Core.Automation;
using LilacMacro.Core.Ocr;
using LilacMacro.Core.Placements;
using LilacMacro.Runtime.Normalization;
using LilacMacro.Runtime.Services;

namespace LilacMacro.App.Views;

public partial class MacroDashboardPage : UserControl
{
    private readonly DeepDebugSessionService _deepDebug;
    private readonly MacroOwnerState _ownerState;
    private readonly WorkspaceController _workspace;
    private readonly OcrRunner _ocr;
    private readonly StoryWireTestRunner _runner;
    private readonly UtilityTaskService _utilities;
    private readonly MacroLobbyResetService _lobbyReset;
    private readonly MacroTaskOptionsFactory _taskOptions;
    private readonly MacroControlCoordinator _control;
    private readonly Stopwatch _runtime = new();
    private readonly Dictionary<PlanTaskPrototype, int> _victories = [];
    private readonly Dictionary<PlanTaskPrototype, int> _defeats = [];
    private readonly Dictionary<PlanTaskPrototype, DateTimeOffset> _blockedUntil = [];
    private readonly Dictionary<PlanTaskPrototype, DateTimeOffset> _utilityDueAt = [];
    private readonly MacroUnattendedRecoveryRunner _recovery;
    private readonly List<RunStatsPoint> _runStats = [];
    private readonly DispatcherTimer _runtimeTimer;
    private DeepDebugScope? _debugScope;
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private bool _runStarting;
    private bool _initialized;
    private PlanTaskPrototype? _currentTask;

    public event Action<bool>? RunningChanged;

    internal MacroDashboardPage(
        DeepDebugSessionService deepDebug,
        MacroOwnerState ownerState,
        ControlSnapshotPollingService control)
    {
        _deepDebug = deepDebug;
        _ownerState = ownerState;
        _control = new MacroControlCoordinator(control, () => ownerState.OnlineFeaturesEnabled);
        _workspace = new WorkspaceController(deepDebug);
        _ocr = new OcrRunner(deepDebug) { KeepLoaded = true };
        _runner = new StoryWireTestRunner(_workspace, _ocr, deepDebug);
        _utilities = new UtilityTaskService(_workspace, _ocr);
        _lobbyReset = new MacroLobbyResetService(
            ownerState,
            _control,
            _workspace,
            _ocr,
            deepDebug,
            AppendLog);
        PlacementSetupStore placements = new(Path.Combine(
            MacroInstanceContext.Current.ConfigurationRoot,
            "placements"));
        _taskOptions = new MacroTaskOptionsFactory(ownerState, placements);
        _recovery = new MacroUnattendedRecoveryRunner(
            _blockedUntil,
            () => _currentTask,
            () => _currentTask = null,
            AppendLog,
            RefreshUpcomingTasks,
            deepDebug);
        InitializeComponent();
        _runtimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _runtimeTimer.Tick += (_, _) => RuntimeText.Text = _runtime.Elapsed.ToString(@"hh\:mm\:ss");
        StatsChart.SetPoints(_runStats);
        PlanCombo.DisplayMemberPath = nameof(PlanPrototype.Name);
        PlanCombo.ItemsSource = ownerState.Plans;
        PlanCombo.SelectedItem = ownerState.SelectedPlan;
        ownerState.SelectedPlanChanged += OwnerState_OnSelectedPlanChanged;
        ApplyLayoutProfile(ownerState.LayoutProfile);
    }

    internal void ApplyLayoutProfile(MacroLayoutProfile profile)
    {
        bool dockAllowed = MacroDisplayPolicy.AllowsDock(profile);
        if (!dockAllowed) RobloxDock.SetRequested(false);
        DockCard.Visibility = dockAllowed ? Visibility.Visible : Visibility.Collapsed;
        DockColumn.Width = dockAllowed ? new GridLength(1396) : new GridLength(0);
        DockSpacerColumn.Width = dockAllowed ? new GridLength(14) : new GridLength(0);
        StatsCard.SetValue(Grid.ColumnProperty, dockAllowed ? 2 : 0);
        StatsCard.SetValue(Grid.ColumnSpanProperty, dockAllowed ? 1 : 3);
        DashboardRoot.MinWidth = dockAllowed ? 1740 : 0;
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
        _ownerState.SelectPlan(plan);
        _currentTask = null;
        RefreshUpcomingTasks(plan);
    }

    private void OwnerState_OnSelectedPlanChanged(object? sender, EventArgs eventArgs)
    {
        if (!ReferenceEquals(PlanCombo.SelectedItem, _ownerState.SelectedPlan))
            PlanCombo.SelectedItem = _ownerState.SelectedPlan;
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
            if (!_control.CanStart(out string unavailableMessage))
            {
                AppToastService.ShowError(
                    "GAME TEMPORARILY UNAVAILABLE",
                    unavailableMessage);
                return;
            }
            PrivateServerRejoinService.Validate(_ownerState.PrivateServerLink);
            await _ownerState.FlushAsync();
            _runCancellation = new CancellationTokenSource();
            _debugScope = await _deepDebug.OpenSessionAsync(
                "macro-runtime",
                new DeepDebugOperationContext("main-macro", new
                {
                    Plan = plan.Name,
                    Instance = MacroInstanceContext.Current.DisplayName,
                }));
            _runtime.Restart();
            _utilityDueAt.Clear();
            _runtimeTimer.Start();
            _runStats.Clear();
            StatsChart.SetPoints(_runStats);
            RefreshRunState(true);
            string device = SelectOcrDevice();
            if (!_initialized)
            {
                await _workspace.InitializeAsync();
                _initialized = true;
            }
            await MacroPlanPreflight.ValidateAsync(
                plan,
                async (task, token) =>
                {
                    if (task.Mode == PlanTaskMode.Utilities)
                    {
                        MacroRuntimeKeySnapshot keys = _ownerState.KeyBindings.Snapshot();
                        if (UtilityTaskPolicy.RequiresAreasMenu(task.Route) && keys.AreasMenu is null)
                            throw new InvalidDataException("Areas menu must have a key for shop and refuel tasks.");
                        return;
                    }
                    _ = await _taskOptions.CreateAsync(task, device, token);
                },
                _runCancellation.Token);
            bool startupSettingsNormalized = false;
            MacroRunTeamState teamState = new();
            HashSet<string> redeemedCodes = new(StringComparer.OrdinalIgnoreCase);
            _runTask = _recovery.RunAsync(
                plan,
                (madeProgress, token) => RunPlanAsync(
                    plan,
                    device,
                    teamState,
                    redeemedCodes,
                    madeProgress,
                    () => startupSettingsNormalized,
                    () => startupSettingsNormalized = true,
                    token),
                _runCancellation.Token);
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

    private async Task RunPlanAsync(
        PlanPrototype plan,
        string device,
        MacroRunTeamState teamState,
        HashSet<string> redeemedCodes,
        Action madeProgress,
        Func<bool> startupSettingsNormalized,
        Action markStartupSettingsNormalized,
        CancellationToken cancellationToken)
    {
        if (!_control.CanContinue(AppendLog)) return;
        bool normalizeStartupSettings = !startupSettingsNormalized();
        await _lobbyReset.ResetAsync(
            device,
            normalizeStartupSettings,
            redeemedCodes,
            cancellationToken);
        if (normalizeStartupSettings) markStartupSettingsNormalized();
        PlanTaskPrototype? repeatedTask = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_control.CanContinue(AppendLog)) return;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            bool repeatedEntry = repeatedTask is not null;
            PlanTaskPrototype? task = repeatedTask ?? MacroPriorityPolicy.SelectEligibleAt(
                    plan,
                    _victories,
                    now,
                    EligibleAt,
                    _control.IsTaskEnabled);
            repeatedTask = null;
            if (task is null)
            {
                PlanTaskPrototype[] pending = MacroPriorityPolicy.Flatten(plan)
                    .Where(candidate => MacroPriorityPolicy.IsPending(candidate, _victories))
                    .ToArray();
                if (pending.Length == 0) return;
                if (!pending.Any(candidate =>
                        _control.IsTaskEnabled(candidate, now)))
                {
                    AppendLog("WAIT | ALL PENDING TASKS TEMPORARILY DISABLED");
                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                    continue;
                }
                DateTimeOffset next = pending
                    .Select(candidate => EligibleAt(candidate, now))
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

            if (task.Mode == PlanTaskMode.Utilities)
            {
                MacroRuntimeKeySnapshot keys = _ownerState.KeyBindings.Snapshot();
                await _utilities.RunAsync(
                    task.Route,
                    task.ShopItemIds,
                    keys.AreasMenu,
                    keys.MacroToggle,
                    device,
                    AppendLog,
                    cancellationToken);
                _utilityDueAt[task] = _control.NextUtilityDue(task, DateTimeOffset.UtcNow);
                _recovery.MarkTaskSucceeded(task);
                madeProgress();
                AppendLog($"UTILITY COMPLETE | NEXT {_utilityDueAt[task]:yyyy-MM-dd HH:mm:ss}Z");
                _currentTask = null;
                RefreshUpcomingTasks(plan);
                continue;
            }

            StoryWireTestOptions options = await _taskOptions.CreateAsync(task, device, cancellationToken);
            if (!_control.IsTeamSwapEnabled(DateTimeOffset.UtcNow) &&
                !teamState.CanReuse(options.TeamNumber))
            {
                DateTimeOffset until = _control.SnapshotExpiry;
                _blockedUntil[task] = until;
                AppendLog($"TASK DEFERRED | TEAM SWAP TEMPORARILY DISABLED | {task.Name}");
                _currentTask = null;
                RefreshUpcomingTasks(plan);
                continue;
            }
            options = options with { SkipTeamLoad = teamState.CanReuse(options.TeamNumber) };
            Progress<StoryWireProgress> progress = new(value =>
            {
                AppendLog($"{StoryWireTestRunner.Format(value.Stage)} | {value.Detail}");
                RuntimeText.Text = _runtime.Elapsed.ToString(@"hh\:mm\:ss");
            });
            StoryWireTestResult result = repeatedEntry
                ? await _runner.RunRepeatedAsync(options, progress, cancellationToken)
                : await _runner.RunAsync(options, progress, cancellationToken);
            if (!result.Succeeded) throw new InvalidOperationException(result.Status);
            teamState.MarkLoaded(options.TeamNumber);

            if (result.UnavailableUntilUtc is DateTimeOffset unavailableUntil)
            {
                _blockedUntil[task] = unavailableUntil;
                AppendLog(result.Status);
                _currentTask = null;
                RefreshUpcomingTasks(plan);
                await _lobbyReset.ResetAsync(
                    device,
                    normalizeStartupSettings: false,
                    redeemedCodes,
                    cancellationToken);
                continue;
            }

            MatchTerminalOutcome outcome = result.Outcome
                ?? throw new InvalidOperationException("The completed match did not return a terminal outcome.");
            bool victory = outcome == MatchTerminalOutcome.Victory;
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
            _recovery.MarkTaskSucceeded(task);
            madeProgress();
            StatsChart.SetPoints(_runStats);
            RefreshUpcomingTasks(plan);

            DateTimeOffset terminalDecisionAt = DateTimeOffset.UtcNow;
            PlanTaskPrototype? nextTask = MacroPriorityPolicy.SelectEligibleAt(
                plan,
                _victories,
                terminalDecisionAt,
                EligibleAt,
                _control.IsTaskEnabled);
            bool modeSupportsRepeat = MacroTaskRepeatPolicy.Supports(task.Mode);
            bool sameTaskSelected = ReferenceEquals(task, nextTask);
            bool hasPendingCodes = _lobbyReset.HasPendingCodes(redeemedCodes, terminalDecisionAt);
            bool shouldRepeat = MatchContinuationPolicy.ShouldRepeat(
                    hasVerifiedTerminalOutcome: true,
                    modeSupportsRepeat,
                    sameTaskSelected) &&
                !hasPendingCodes;
            _deepDebug.RecordEvent("macro", "terminal_continuation_decided", new
            {
                ObservedAtUtc = terminalDecisionAt,
                Outcome = outcome.ToString(),
                CurrentTask = task.Name,
                NextTask = nextTask?.Name,
                ModeSupportsRepeat = modeSupportsRepeat,
                SameTaskSelected = sameTaskSelected,
                HasPendingCodes = hasPendingCodes,
                Decision = shouldRepeat ? "repeat_stage" : "lobby_reset",
            });
            if (shouldRepeat)
            {
                try
                {
                    await _runner.RepeatStageAsync(outcome, options, progress, cancellationToken);
                    AppendLog("REPEAT CONTINUATION | TEAM + CAMERA RETAINED");
                    repeatedTask = task;
                    continue;
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    AppendLog($"REPEAT UNAVAILABLE | LOBBY RESET | {error.Message}");
                }
            }

            AppendLog(
                $"LOBBY CONTINUATION | NEXT {nextTask?.Name ?? "NONE"} | " +
                $"SAME TASK {sameTaskSelected} | MODE REPEAT {modeSupportsRepeat} | " +
                $"PENDING CODES {hasPendingCodes}");

            _currentTask = null;
            await _lobbyReset.ResetAsync(
                device,
                normalizeStartupSettings: false,
                redeemedCodes,
                cancellationToken);
        }
    }

    private string SelectOcrDevice()
    {
        if (_ocr.IsDeviceReady(OcrRunner.GpuDevice)) return OcrRunner.GpuDevice;
        if (_ocr.IsDeviceReady(OcrRunner.CpuDevice)) return OcrRunner.CpuDevice;
        throw new InvalidOperationException("Set up OCR in Dataset Builder before starting the macro.");
    }

    private DateTimeOffset EligibleAt(PlanTaskPrototype task, DateTimeOffset fallback)
    {
        DateTimeOffset eligible = _blockedUntil.GetValueOrDefault(task, fallback);
        if (_utilityDueAt.TryGetValue(task, out DateTimeOffset utilityDue) && utilityDue > eligible)
            eligible = utilityDue;
        return eligible;
    }

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
        RunningChanged?.Invoke(running);
    }

    private void RefreshUpcomingTasks(PlanPrototype plan)
    {
        UpcomingTasksList.ItemsSource = UpcomingTaskRowFactory.Build(
            plan,
            _currentTask,
            _victories,
            DateTimeOffset.UtcNow,
            EligibleAt);
        /* Legacy inline row projection removed after the view model factory extraction.
                    ? $"CURRENT · PRIORITY {task.Priority}"
                    : $"{task.ModeLabel.ToUpperInvariant()} · PRIORITY {task.Priority}",
        */
    }

    private void AppendLog(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted)
                _ = Dispatcher.BeginInvoke(() => AppendLog(message));
            return;
        }

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
