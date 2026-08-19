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
using LilacMacro.Windows;

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
    private readonly Dictionary<PlanLoopPrototype, int> _completedLoopRuns = [];
    private readonly Dictionary<PlanTaskPrototype, DateTimeOffset> _blockedUntil = [];
    private readonly Dictionary<PlanTaskPrototype, DateTimeOffset> _utilityDueAt = [];
    private readonly MacroUnattendedRecoveryRunner _recovery;
    private readonly ConfigurationMutationGate _configurationGate = ConfigurationMutationGate.CreateDefault();
    private readonly List<RunStatsPoint> _runStats = [];
    private readonly DispatcherTimer _runtimeTimer;
    private readonly DispatcherTimer _runLogTimer;
    private readonly RunLogBuffer _runLog = new();
    private DeepDebugScope? _debugScope;
    private readonly IDisposable _deepDebugFrameCaptureRegistration;
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private bool _runStarting;
    private bool _initialized;
    private PlanTaskPrototype? _currentTask;
    private IDisposable? _configurationRunLease;

    public event Action<bool>? RunningChanged;

    internal bool IsRunning => _runStarting || _runTask is not null || _runCancellation is not null;

    internal MacroDashboardPage(
        DeepDebugSessionService deepDebug,
        MacroOwnerState ownerState,
        ControlSnapshotPollingService control)
    {
        _deepDebug = deepDebug;
        _ownerState = ownerState;
        _control = new MacroControlCoordinator(control, () => ownerState.OnlineFeaturesEnabled);
        _workspace = new WorkspaceController(deepDebug);
        _deepDebugFrameCaptureRegistration = RegisterDeepDebugFrameCaptureProvider();
        _ocr = new OcrRunner(deepDebug) { KeepLoaded = true };
        _runner = new StoryWireTestRunner(_workspace, _ocr, deepDebug);
        _utilities = new UtilityTaskService(_workspace, _ocr);
        _lobbyReset = new MacroLobbyResetService(
            ownerState,
            _control,
            _workspace,
            _ocr,
            deepDebug,
            AppendLog,
            () => _ = Dispatcher.BeginInvoke(RobloxDock.ReacquireAfterRobloxLaunch));
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
            deepDebug,
            new MacroInternetConnectivityGate(
                new InternetConnectivityProbe().IsAvailable,
                AppendLog).WaitUntilAvailableAsync,
            NotifyDiscordRecovery);
        InitializeComponent();
        InitializeRuntimeProgressPersistence();
        InitializeDiscordEvents();
        _runtimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _runtimeTimer.Tick += (_, _) => RuntimeText.Text = FormatRuntime(_runtime.Elapsed);
        _runLogTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _runLogTimer.Tick += RunLogTimer_OnTick;
        _runLogTimer.Start();
        StatsChart.SetPoints(_runStats);
        PlanCombo.DisplayMemberPath = nameof(PlanPrototype.Name);
        PlanCombo.ItemsSource = ownerState.Plans;
        PlanCombo.SelectedItem = ownerState.SelectedPlan;
        ownerState.SelectedPlanChanged += OwnerState_OnSelectedPlanChanged;
        ownerState.PlansChanged += OwnerState_OnPlansChanged;
        ApplyLayoutProfile(ownerState.LayoutProfile);
        _ocrReady = _ocr.IsDeviceReady(OcrRunner.GpuDevice) || _ocr.IsDeviceReady(OcrRunner.CpuDevice);
        _ocrSetupFailed = !_ocrReady;
        UpdateStartButtonState();
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
    private void OwnerState_OnPlansChanged(object? sender, EventArgs eventArgs)
    {
        if (PlanCombo.SelectedItem is not PlanPrototype plan) return;
        PlanCombo.Items.Refresh();
        PlanCombo.SelectedItem = null;
        PlanCombo.SelectedItem = plan;
    }
    private async void StartButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (!_ocrReady)
        {
            await EnsureOcrReadyAsync();
            return;
        }
        await StartMacroAsync();
    }
    internal void ToggleRunFromHotkey()
    {
        if (_runTask is not null || _runStarting)
        {
            _runCancellation?.Cancel();
            return;
        }
        if (!_ocrReady)
        {
            AppToastService.ShowError("OCR SETUP REQUIRED", "Use SET UP OCR before starting the Macro.");
            return;
        }
        _ = StartMacroAsync();
    }

    private async Task StartMacroAsync()
    {
        if (_runTask is not null || _runStarting || PlanCombo.SelectedItem is not PlanPrototype plan) return;
        _runStarting = true;
        UpdateStartButtonState();
        try
        {
            await EnsureRuntimeProgressLoadedAsync();
            if (!_ocrReady)
            {
                AppToastService.ShowError("OCR SETUP REQUIRED", "Use SET UP OCR before starting the Macro.");
                return;
            }
            if (!_control.CanStart(out string unavailableMessage))
            {
                AppToastService.ShowError(
                    "GAME TEMPORARILY UNAVAILABLE",
                    unavailableMessage);
                return;
            }
            PrivateServerRejoinService.Validate(_ownerState.PrivateServerLink);
            _configurationRunLease = _configurationGate.AcquireRunLease();
            await _ownerState.FlushAsync();
            _runCancellation = new CancellationTokenSource();
            _recovery.ResetForNewRun();
            _debugScope = await _deepDebug.OpenSessionAsync(
                "macro-runtime",
                new DeepDebugOperationContext("main-macro", new
                {
                    Plan = plan.Name,
                    Instance = MacroInstanceContext.Current.DisplayName,
                }));
            _runtime.Restart();
            _runtimeTimer.Start();
            _runStats.Clear();
            StatsChart.SetPoints(_runStats);
            RefreshRunState(true);
            BeginDiscordRun(plan);
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
            NotifyDiscordRunStopped(plan, "Plan complete.");
        }
        catch (OperationCanceledException)
        {
            AppendLog("STOPPED");
            NotifyDiscordRunStopped(plan, "Stopped by the user.");
        }
        catch (Exception error)
        {
            AppToastService.ShowError("MACRO STOPPED", error.Message);
            AppendLog($"ERROR {error.Message}");
            _deepDebug.RecordEvent("macro", "runtime_error", new { Error = error.ToString() });
            NotifyDiscordTerminalFailure(plan);
        }
        finally
        {
            _runStarting = false;
            _runtime.Stop();
            _runtimeTimer.Stop();
            _runTask = null;
            _runCancellation?.Dispose();
            _runCancellation = null;
            _configurationRunLease?.Dispose();
            _configurationRunLease = null;
            await FlushRuntimeProgressAsync();
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
        PlanTaskPrototype? lobbyHandoffFrom = _recovery.TakeOpportunisticHandoff();
        PlanTaskPrototype? repeatedTask = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_control.CanContinue(AppendLog)) return;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            bool repeatedEntry = repeatedTask is not null;
            PlanTaskPrototype? task = repeatedTask ?? SelectEligibleTask(plan, now);
            repeatedTask = null;
            if (task is null)
            {
                PlanTaskPrototype[] allPending = MacroPriorityPolicy.Flatten(plan, _completedLoopRuns)
                    .Where(candidate => MacroPriorityPolicy.IsPending(candidate, _victories))
                    .ToArray();
                if (allPending.Length == 0) return;
                PlanTaskPrototype[] pending = allPending
                    .Where(candidate => !_recovery.IsIndefinitelyQuarantined(candidate))
                    .ToArray();
                if (pending.Length == 0)
                {
                    AppendLog("WAIT | ALL PENDING UTILITIES QUARANTINED");
                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                    continue;
                }
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

            if (lobbyHandoffFrom is PlanTaskPrototype previousTask)
            {
                lobbyHandoffFrom = null;
                await RunLobbyHandoffOpportunityAsync(
                    plan,
                    previousTask,
                    task,
                    device,
                    madeProgress,
                    cancellationToken);
            }

            _currentTask = task;
            NotifyDiscordTaskChanged(plan, task);
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
                QueueRuntimeProgressSave();
                AppendLog($"UTILITY COMPLETE | NEXT {_utilityDueAt[task]:yyyy-MM-dd HH:mm:ss}Z");
                _currentTask = null;
                lobbyHandoffFrom = task;
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
                RuntimeText.Text = FormatRuntime(_runtime.Elapsed);
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
                lobbyHandoffFrom = task;
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
            NotifyDiscordOutcome(plan, task, victory);
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

            if (victory && MacroLoopProgressReporter.AdvanceAndReport(
                    plan, _victories, _completedLoopRuns, _deepDebug, AppendLog))
                RefreshUpcomingTasks(plan);
            QueueRuntimeProgressSave();

            DateTimeOffset terminalDecisionAt = DateTimeOffset.UtcNow;
            PlanTaskPrototype? nextTask = MacroPriorityPolicy.SelectEligibleAt(
                plan,
                _victories,
                _completedLoopRuns,
                terminalDecisionAt,
                EligibleAt,
                IsTaskEnabledForSelection);
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
                Decision = shouldRepeat
                    ? result.RepeatedPrestartReady ? "verified_prestart" : "repeat_stage"
                    : "lobby_reset",
            });
            if (shouldRepeat)
            {
                if (result.RepeatedPrestartReady)
                {
                    AppendLog("INFINITE CONTINUATION | VERIFIED PRESTART | TEAM + CAMERA RETAINED");
                    repeatedTask = task;
                    continue;
                }
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
            lobbyHandoffFrom = task;
        }
    }

}
