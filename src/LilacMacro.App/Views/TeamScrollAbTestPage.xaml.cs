using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LilacMacro.App.Debugging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;

namespace LilacMacro.App.Views;

public partial class TeamScrollAbTestPage : UserControl, IWorkspacePage
{
    private readonly TeamScrollAbTestRunner _runner;
    private readonly ObservableCollection<TrialRow> _trials = [];
    private readonly string _device;
    private CancellationTokenSource? _cancellation;
    private string? _outputDirectory;

    internal TeamScrollAbTestPage(
        WorkspaceController workspace,
        OcrRunner ocr,
        string device)
    {
        _runner = new TeamScrollAbTestRunner(workspace, ocr);
        _device = device;
        InitializeComponent();
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

    private async void RunDragButton_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(TeamScrollTestMethod.Drag);

    private async void RunScrollButton_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunAsync(TeamScrollTestMethod.Scroll);

    private void StopButton_OnClick(object sender, RoutedEventArgs eventArgs) => _cancellation?.Cancel();

    private async Task RunAsync(TeamScrollTestMethod method)
    {
        if (IsRunning) return;
        try
        {
            int trials = ParseBounded(TrialsBox.Text, "Trials", 1, 1000);
            int scrollUnits = ParseBounded(ScrollUnitsBox.Text, "Down scroll units", 1, 10000);
            int scrollIncrement = ParseBounded(
                ScrollIncrementBox.Text,
                "Scroll increment",
                0,
                10000);
            ResetMethod(method);
            _outputDirectory = null;
            _cancellation = new CancellationTokenSource();
            UpdateControls();
            SetStatus("CALIBRATING", "YellowBrush");

            TeamScrollTestResult result = await _runner.RunAsync(
                method,
                trials,
                scrollUnits,
                scrollIncrement,
                _device,
                new Progress<TeamScrollTestProgress>(ShowProgress),
                _cancellation.Token);
            _outputDirectory = result.OutputDirectory;
            OutputPathText.Text = result.OutputDirectory;
            ShowSummary(method, result.Trials);
            SetStatus(result.Status, result.Status == "COMPLETE" ? "SuccessBrush" : "YellowBrush");
        }
        catch (OperationCanceledException)
        {
            SetStatus("STOPPED", "YellowBrush");
        }
        catch (Exception error)
        {
            SetStatus("ERROR", "DangerBrush");
            OutputPathText.Text = error.Message;
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            UpdateControls();
        }
    }

    private void ShowProgress(TeamScrollTestProgress progress)
    {
        ProgressText.Text = $"{progress.Completed} / {progress.Total}";
        StatusText.Text = progress.Detail;
        if (progress.Trial is null) return;
        _trials.Add(new TrialRow(
            progress.Trial.Trial,
            progress.Trial.Method.ToString().ToUpperInvariant(),
            progress.Trial.ScrollUnits?.ToString(CultureInfo.InvariantCulture) ?? "—",
            progress.Trial.Position is null ? "—" : progress.Trial.Position.Value.ToString("P1", CultureInfo.InvariantCulture),
            progress.Trial.ThumbBounds is null ? "—" : Format(progress.Trial.ThumbBounds.Value),
            progress.Trial.Status));
    }

    private void ShowSummary(
        TeamScrollTestMethod method,
        IReadOnlyList<TeamScrollTrialResult> trials)
    {
        double[] positions = trials
            .Where(trial => trial.Position is not null)
            .Select(trial => trial.Position!.Value)
            .ToArray();
        double mean = positions.Length == 0 ? 0 : positions.Average();
        double spread = positions.Length == 0
            ? 0
            : Math.Sqrt(positions.Average(value => Math.Pow(value - mean, 2)));
        string summary = positions.Length == 0
            ? $"— · {trials.Count} MISS"
            : $"{mean.ToString("P1", CultureInfo.InvariantCulture)} · " +
              $"σ {spread.ToString("P2", CultureInfo.InvariantCulture)} · " +
              $"{trials.Count - positions.Length} MISS";
        if (method == TeamScrollTestMethod.Drag) DragSummaryText.Text = summary;
        else ScrollSummaryText.Text = summary;
    }

    private void ResetMethod(TeamScrollTestMethod method)
    {
        TrialRow[] stale = _trials
            .Where(trial => trial.Method == method.ToString().ToUpperInvariant())
            .ToArray();
        foreach (TrialRow trial in stale) _trials.Remove(trial);
        if (method == TeamScrollTestMethod.Drag) DragSummaryText.Text = "—";
        else ScrollSummaryText.Text = "—";
        ProgressText.Text = "0 / 0";
        OutputPathText.Text = "NO RESULTS";
    }

    private void UpdateControls()
    {
        RunDragButton.IsEnabled = !IsRunning;
        RunScrollButton.IsEnabled = !IsRunning;
        StopButton.IsEnabled = IsRunning;
        TrialsBox.IsEnabled = !IsRunning;
        ScrollUnitsBox.IsEnabled = !IsRunning;
        ScrollIncrementBox.IsEnabled = !IsRunning;
        OpenResultsButton.IsEnabled = !IsRunning && Directory.Exists(_outputDirectory);
    }

    private void SetStatus(string text, string brush)
    {
        StatusText.Text = text;
        StatusBand.SetResourceReference(Border.BackgroundProperty, brush);
    }

    private void OpenResultsButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (!Directory.Exists(_outputDirectory)) return;
        Process.Start(new ProcessStartInfo(_outputDirectory) { UseShellExecute = true });
    }

    private static int ParseBounded(string value, string name, int minimum, int maximum)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ||
            parsed < minimum || parsed > maximum)
        {
            throw new InvalidOperationException($"{name} must be between {minimum} and {maximum}.");
        }
        return parsed;
    }

    private static string Format(LilacMacro.Core.Geometry.PixelRect bounds) =>
        $"[{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}]";

    private sealed record TrialRow(
        int Trial,
        string Method,
        string ScrollUnits,
        string Position,
        string Bounds,
        string Status);
}
