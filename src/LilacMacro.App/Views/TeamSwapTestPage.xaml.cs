using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LilacMacro.App.Debugging;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;

namespace LilacMacro.App.Views;

public partial class TeamSwapTestPage : UserControl, IWorkspacePage
{
    private readonly TeamSwapTestRunner _runner;
    private readonly DeepDebugSessionService _deepDebug;
    private readonly ObservableCollection<TrialRow> _trials = [];
    private readonly string _device;
    private CancellationTokenSource? _cancellation;

    internal TeamSwapTestPage(
        WorkspaceController workspace,
        OcrRunner ocr,
        DeepDebugSessionService deepDebug,
        string device)
    {
        _deepDebug = deepDebug;
        _device = device;
        _runner = new TeamSwapTestRunner(new DebugOcrController(workspace, ocr), deepDebug);
        InitializeComponent();
        TrialList.ItemsSource = _trials;
    }

    public bool IsRunning => _cancellation is not null;

    public event EventHandler? RunningChanged;

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

    private void StopButton_OnClick(object sender, RoutedEventArgs eventArgs) => RequestStop();

    public bool RequestStop()
    {
        if (_cancellation is null) return false;
        _cancellation.Cancel();
        return true;
    }

    private async Task RunAsync()
    {
        if (IsRunning) return;
        try
        {
            int trials = ParseTrials(TrialsBox.Text);
            _trials.Clear();
            PassedText.Text = "0";
            FailedText.Text = "0";
            ProgressText.Text = $"0 / {trials}";
            _cancellation = new CancellationTokenSource();
            RunningChanged?.Invoke(this, EventArgs.Empty);
            UpdateControls();
            SetStatus("RUNNING", "YellowBrush");

            TeamSwapTestResult result = await _deepDebug.RunOperationAsync(
                "team-swap-test",
                new DeepDebugOperationContext(
                    "runtime-lab",
                    new { Trials = trials, Device = _device, Teams = "balanced-random-1-8" }),
                token => _runner.RunAsync(
                    trials,
                    _device,
                    new Progress<TeamSwapTestProgress>(ShowProgress),
                    token),
                _cancellation.Token);
            SetStatus(result.Failed == 0 ? "PASSED" : "COMPLETE WITH FAILURES",
                result.Failed == 0 ? "SuccessBrush" : "DangerBrush");
        }
        catch (OperationCanceledException)
        {
            SetStatus("STOPPED", "YellowBrush");
        }
        catch (Exception error)
        {
            SetStatus("ERROR", "DangerBrush");
            _trials.Add(new TrialRow("—", "—", "ERROR", "—", error.Message));
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            RunningChanged?.Invoke(this, EventArgs.Empty);
            UpdateControls();
        }
    }

    private void ShowProgress(TeamSwapTestProgress progress)
    {
        ProgressText.Text = $"{progress.Completed} / {progress.Total}";
        StatusText.Text = progress.Detail;
        if (progress.Trial is null) return;
        _trials.Add(new TrialRow(
            progress.Trial.Trial.ToString(CultureInfo.InvariantCulture),
            progress.Trial.Team.ToString(CultureInfo.InvariantCulture),
            progress.Trial.Succeeded ? "PASS" : "FAIL",
            $"{progress.Trial.ElapsedMilliseconds} MS",
            progress.Trial.Status));
        int passed = _trials.Count(trial => trial.Result == "PASS");
        PassedText.Text = passed.ToString(CultureInfo.InvariantCulture);
        FailedText.Text = (_trials.Count - passed).ToString(CultureInfo.InvariantCulture);
        TrialList.ScrollIntoView(_trials[^1]);
    }

    private void UpdateControls()
    {
        RunButton.IsEnabled = !IsRunning;
        StopButton.IsEnabled = IsRunning;
        TrialsBox.IsEnabled = !IsRunning;
    }

    private void SetStatus(string text, string brush)
    {
        StatusText.Text = text;
        StatusBand.SetResourceReference(Border.BackgroundProperty, brush);
    }

    private static int ParseTrials(string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ||
            parsed is < 1 or > 1000)
        {
            throw new InvalidOperationException("Trials must be between 1 and 1000.");
        }
        return parsed;
    }

    private sealed record TrialRow(string Trial, string Team, string Result, string Time, string Status);
}
