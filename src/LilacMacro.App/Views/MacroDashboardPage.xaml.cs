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

namespace LilacMacro.App.Views;

public partial class MacroDashboardPage : UserControl
{
    private readonly DeepDebugSessionService _deepDebug;
    private readonly MacroOwnerState _ownerState;
    private readonly WorkspaceController _workspace;
    private readonly OcrRunner _ocr;
    private readonly StoryWireTestRunner _runner;
    private readonly PrivateServerRejoinService _rejoin;
    private readonly UiScaleNormalizer _uiScale;
    private readonly GameSettingsNormalizer _gameSettings;
    private readonly UtilityTaskService _utilities;
    private readonly MacroTaskOptionsFactory _taskOptions;
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
        MacroOwnerState ownerState)
    {
        _deepDebug = deepDebug;
        _ownerState = ownerState;
        _workspace = new WorkspaceController(deepDebug);
        _ocr = new OcrRunner(deepDebug) { KeepLoaded = true };
        _runner = new StoryWireTestRunner(_workspace, _ocr, deepDebug);
        _rejoin = new PrivateServerRejoinService(_workspace, _ocr);
        _uiScale = new UiScaleNormalizer(_workspace, _ocr, deepDebug);
        _gameSettings = new GameSettingsNormalizer(_workspace, deepDebug);
        _utilities = new UtilityTaskService(_workspace, _ocr);
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
            _runTask = _recovery.RunAsync(
                plan,
                (madeProgress, token) => RunPlanAsync(
                    plan,
                    device,
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
        Action madeProgress,
        Func<bool> startupSettingsNormalized,
        Action markStartupSettingsNormalized,
        CancellationToken cancellationToken)
    {
        bool normalizeStartupSettings = !startupSettingsNormalized();
        await ResetLobbyAsync(device, normalizeStartupSettings, cancellationToken);
        if (normalizeStartupSettings) markStartupSettingsNormalized();
        PlanTaskPrototype? repeatedTask = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            bool repeatedEntry = repeatedTask is not null;
            PlanTaskPrototype? task = repeatedTask ?? MacroPriorityPolicy.Select(
                    plan,
                    _victories,
                    candidate => now >= EligibleAt(candidate, now));
            repeatedTask = null;
            if (task is null)
            {
                PlanTaskPrototype[] pending = MacroPriorityPolicy.Flatten(plan)
                    .Where(candidate => MacroPriorityPolicy.IsPending(candidate, _victories))
                    .ToArray();
                if (pending.Length == 0) return;
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
                _utilityDueAt[task] = UtilityTaskPolicy.NextDue(
                    task.Route, DateTimeOffset.UtcNow, task.Target);
                _recovery.MarkTaskSucceeded(task);
                madeProgress();
                AppendLog($"UTILITY COMPLETE | NEXT {_utilityDueAt[task]:yyyy-MM-dd HH:mm:ss}Z");
                _currentTask = null;
                RefreshUpcomingTasks(plan);
                continue;
            }

            StoryWireTestOptions options = await _taskOptions.CreateAsync(task, device, cancellationToken);
            Progress<StoryWireProgress> progress = new(value =>
            {
                AppendLog($"{StoryWireTestRunner.Format(value.Stage)} | {value.Detail}");
                RuntimeText.Text = _runtime.Elapsed.ToString(@"hh\:mm\:ss");
            });
            StoryWireTestResult result = repeatedEntry
                ? await _runner.RunRepeatedAsync(options, progress, cancellationToken)
                : await _runner.RunAsync(options, progress, cancellationToken);
            if (!result.Succeeded) throw new InvalidOperationException(result.Status);

            if (result.UnavailableUntilUtc is DateTimeOffset unavailableUntil)
            {
                _blockedUntil[task] = unavailableUntil;
                AppendLog(result.Status);
                _currentTask = null;
                RefreshUpcomingTasks(plan);
                await ResetLobbyAsync(device, normalizeStartupSettings: false, cancellationToken);
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

            PlanTaskPrototype? nextTask = MacroPriorityPolicy.Select(
                plan,
                _victories,
                candidate => DateTimeOffset.UtcNow >= EligibleAt(candidate, DateTimeOffset.UtcNow));
            if (MatchContinuationPolicy.ShouldRepeat(
                    hasVerifiedTerminalOutcome: true,
                    modeSupportsRepeat: task.Mode is PlanTaskMode.Story or PlanTaskMode.Raid or PlanTaskMode.Event,
                    sameTaskSelected: ReferenceEquals(task, nextTask)))
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

            _currentTask = null;
            await ResetLobbyAsync(device, normalizeStartupSettings: false, cancellationToken);
        }
    }

    private async Task ResetLobbyAsync(
        string device,
        bool normalizeStartupSettings,
        CancellationToken cancellationToken)
    {
        await _rejoin.RejoinAndVerifyLobbyAsync(
            _ownerState.PrivateServerLink,
            device,
            AppendLog,
            cancellationToken);
        if (!normalizeStartupSettings) return;
        await _uiScale.NormalizeAsync(device, AppendLog, cancellationToken);
        await _gameSettings.NormalizeAsync(AppendLog, cancellationToken);
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
        IReadOnlyList<PlanTaskPrototype> tasks = MacroPriorityPolicy.Flatten(plan);
        List<UpcomingTaskRow> rows = tasks
            .Where(task => MacroPriorityPolicy.IsPending(task, _victories))
            .Select((task, index) => new UpcomingTaskRow(
                index + 1,
                task.Name,
                ReferenceEquals(task, _currentTask)
                    ? $"CURRENT · PRIORITY {task.Priority}"
                    : $"{task.ModeLabel.ToUpperInvariant()} · PRIORITY {task.Priority}",
                EligibleAt(task, DateTimeOffset.UtcNow) is DateTimeOffset until && until > DateTimeOffset.UtcNow
                    ? $"NEXT {until:MM-dd HH:mm}Z"
                    : task.Mode is PlanTaskMode.Utilities or PlanTaskMode.Challenge
                    ? task.TargetLabel
                    : $"{_victories.GetValueOrDefault(task)}/{task.Target} W"))
            .ToList();
        UpcomingTasksList.ItemsSource = rows;
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

internal sealed record UpcomingTaskRow(int Position, string Name, string Detail, string Progress);
