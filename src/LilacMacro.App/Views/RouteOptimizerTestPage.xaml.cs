using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LilacMacro.App.Debugging;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Views;

public partial class RouteOptimizerTestPage : UserControl, IStoppableWorkspacePage
{
    private static readonly string[] Targets =
    [
        "Fuel Cell",
        "Equipment Scrap",
        "Equipment Reroll",
        "Equipment Lock",
        "Expedition Coin",
    ];
    private static readonly string[] Difficulties = ["Difficulty 1", "Difficulty 2", "Difficulty 3"];

    private readonly RouteOptimizerTestRunner _runner;
    private readonly DeepDebugSessionService _deepDebug;
    private readonly ObservableCollection<TrialRow> _trials = [];
    private readonly string _device;
    private CancellationTokenSource? _cancellation;

    internal RouteOptimizerTestPage(
        WorkspaceController workspace,
        OcrRunner ocr,
        DeepDebugSessionService deepDebug,
        string device)
    {
        _deepDebug = deepDebug;
        _device = device;
        _runner = new RouteOptimizerTestRunner(
            new ExpeditionRewardPoolService(workspace, ocr),
            new ExpeditionSettingsService(workspace, ocr),
            deepDebug,
            new ExpeditionRewardProfileStore());
        InitializeComponent();
        DifficultyBox.ItemsSource = Difficulties;
        DifficultyBox.SelectedIndex = 0;
        TargetBox.ItemsSource = Targets;
        TargetBox.SelectedIndex = 0;
        TrialList.ItemsSource = _trials;
    }

    public bool IsRunning => _cancellation is not null;

    public Task RefreshAsync()
    {
        UpdateControls();
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cancellation is null) return;
        _cancellation.Cancel();
        while (_cancellation is not null) await Task.Delay(20);
    }

    private async void RunButton_OnClick(object sender, RoutedEventArgs eventArgs) => await RunAsync();

    private void StopButton_OnClick(object sender, RoutedEventArgs eventArgs) => _cancellation?.Cancel();

    private async Task RunAsync()
    {
        if (IsRunning) return;
        try
        {
            int trials = ParseTrials(TrialsBox.Text);
            int difficulty = DifficultyBox.SelectedIndex + 1;
            ExpeditionRewardResource target = SelectedTarget();
            _trials.Clear();
            AcceptedText.Text = "0";
            RerolledText.Text = "0";
            ProgressText.Text = $"0 / {trials}";
            _cancellation = new CancellationTokenSource();
            UpdateControls();
            SetStatus("RUNNING", "YellowBrush");

            RouteOptimizerTestResult result = await _deepDebug.RunOperationAsync(
                "route-optimizer-test",
                new DeepDebugOperationContext(
                    "runtime-lab",
                    new { Trials = trials, Difficulty = difficulty, Target = target.ToString(), Device = _device }),
                token => _runner.RunAsync(
                    trials,
                    difficulty,
                    target,
                    _device,
                    new Progress<RouteOptimizerTestProgress>(ShowProgress),
                    token),
                _cancellation.Token);
            SetStatus("COMPLETE", "SuccessBrush");
            AcceptedText.Text = result.Accepted.ToString(CultureInfo.InvariantCulture);
            RerolledText.Text = result.Rerolled.ToString(CultureInfo.InvariantCulture);
        }
        catch (OperationCanceledException)
        {
            SetStatus("STOPPED", "YellowBrush");
        }
        catch (Exception error)
        {
            SetStatus("ERROR", "DangerBrush");
            _trials.Add(new TrialRow("-", "-", "-", "ERROR", error.Message, "-"));
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            UpdateControls();
        }
    }

    private void ShowProgress(RouteOptimizerTestProgress progress)
    {
        ProgressText.Text = $"{progress.Completed} / {progress.Total}";
        StatusText.Text = progress.Detail;
        if (progress.Trial is null) return;
        _trials.Add(new TrialRow(
            progress.Trial.Trial.ToString(CultureInfo.InvariantCulture),
            progress.Trial.Quantity?.ToString(CultureInfo.InvariantCulture) ?? "-",
            progress.Trial.Threshold?.ToString(CultureInfo.InvariantCulture) ?? "LEARNING",
            progress.Trial.Decision,
            progress.Trial.Error ?? string.Join(" | ", progress.Trial.OcrText),
            progress.Trial.RerollMilliseconds is long milliseconds ? $"{milliseconds} MS" : "-"));
        int accepted = _trials.Count(trial => trial.Decision == "ACCEPT");
        AcceptedText.Text = accepted.ToString(CultureInfo.InvariantCulture);
        RerolledText.Text = _trials.Count(trial => trial.Decision == "REROLL")
            .ToString(CultureInfo.InvariantCulture);
        TrialList.ScrollIntoView(_trials[^1]);
    }

    private void UpdateControls()
    {
        RunButton.IsEnabled = !IsRunning;
        StopButton.IsEnabled = IsRunning;
        TrialsBox.IsEnabled = !IsRunning;
        DifficultyBox.IsEnabled = !IsRunning;
        TargetBox.IsEnabled = !IsRunning;
    }

    private void SetStatus(string text, string brush)
    {
        StatusText.Text = text;
        StatusBand.SetResourceReference(Border.BackgroundProperty, brush);
    }

    private ExpeditionRewardResource SelectedTarget() =>
        ExpeditionRewardPolicy.ParseResource(TargetBox.SelectedItem as string ?? Targets[0]);

    private static int ParseTrials(string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            throw new InvalidOperationException("Trials must be a whole number.");
        try { return ExpeditionRewardPolicy.ValidateTestTrials(parsed); }
        catch (InvalidDataException error) { throw new InvalidOperationException(error.Message, error); }
    }

    private sealed record TrialRow(
        string Trial,
        string Quantity,
        string Threshold,
        string Decision,
        string Ocr,
        string Time);
}
