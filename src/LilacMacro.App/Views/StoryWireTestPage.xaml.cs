using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LilacMacro.App.Debugging;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Ocr;
using LilacMacro.Core.Vision;

namespace LilacMacro.App.Views;

public partial class StoryWireTestPage : UserControl, IStoppableWorkspacePage
{
    private readonly OcrRunner _ocr;
    private readonly StoryWireTestRunner _runner;
    private readonly DeepDebugSessionService _deepDebug;
    private readonly ObservableCollection<StoryWireStageItem> _stages;
    private readonly ObservableCollection<string> _events = [];
    private readonly ObservableCollection<WireVisualComparison> _comparisons = [];
    private CancellationTokenSource? _runCancellation;
    private string _device;

    internal StoryWireTestPage(
        WorkspaceController workspace,
        OcrRunner ocr,
        DeepDebugSessionService deepDebug,
        string defaultOcrDevice)
    {
        _ocr = ocr;
        _deepDebug = deepDebug;
        _device = defaultOcrDevice;
        _runner = new StoryWireTestRunner(workspace, ocr, deepDebug);
        _stages = [];
        InitializeComponent();
        StageList.ItemsSource = _stages;
        EventLog.ItemsSource = _events;
        ComparisonList.ItemsSource = _comparisons;
        for (char key = 'A'; key <= 'Z'; key++) UnitsKeyBox.Items.Add(key.ToString());
        UnitsKeyBox.SelectedItem = "H";
        UpdateModeChoices();
        RefreshStages();
    }

    public bool IsRunning => _runCancellation is not null;

    public Task RefreshAsync()
    {
        KeepLoadedToggle.IsChecked = _ocr.KeepLoaded;
        UpdateControls();
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_runCancellation is null) return;
        _runCancellation.Cancel();
        while (_runCancellation is not null) await Task.Delay(20);
    }

    private async void RunButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (IsRunning) return;
        ResetStages();
        _events.Clear();
        _comparisons.Clear();
        ClearComparisonPreview();
        _runCancellation = new CancellationTokenSource();
        UpdateControls();
        SetStatus("RUNNING", "YellowBrush");
        StoryWireTestOptions options = ReadOptions();
        AddEvent(options.Mode == DebugEvidenceMode.Ocr
            ? "WIRE START | OCR"
            : "WIRE START | IMAGE + OCR FALLBACK");
        try
        {
            StoryWireTestResult result = await _deepDebug.RunOperationAsync(
                "story-wire-test",
                new DeepDebugOperationContext("dataset-builder", options),
                token => _runner.RunAsync(
                    options,
                    new Progress<StoryWireProgress>(ShowProgress),
                    token),
                _runCancellation.Token);
            SetStatus(result.Succeeded ? "PASSED" : "BLOCKED", result.Succeeded ? "SuccessBrush" : "DangerBrush");
            AddEvent(result.Status);
        }
        catch (OperationCanceledException)
        {
            SetStatus("STOPPED", "YellowBrush");
            AddEvent("WIRE STOPPED");
        }
        catch (Exception error)
        {
            SetStatus("ERROR", "DangerBrush");
            AddEvent($"ERROR {error.Message}");
        }
        finally
        {
            _runCancellation.Dispose();
            _runCancellation = null;
            UpdateControls();
        }
    }

    private void StopButton_OnClick(object sender, RoutedEventArgs eventArgs) => _runCancellation?.Cancel();

    private void ShowProgress(StoryWireProgress progress)
    {
        StoryWireStageItem item = _stages.Single(candidate => candidate.Stage == progress.Stage);
        item.Status = progress.Status;
        item.Detail = progress.Detail;
        foreach (string entry in progress.Events) AddEvent($"{item.Name}  {entry}");
        WireVisualComparison? newest = null;
        foreach (WireVisualComparison comparison in progress.VisualComparisons ?? [])
        {
            WireVisualComparison? prior = _comparisons.FirstOrDefault(candidate =>
                candidate.State == comparison.State && candidate.Label == comparison.Label);
            if (prior is not null) _comparisons.Remove(prior);
            _comparisons.Insert(0, comparison);
            newest = comparison;
        }
        if (newest is not null) ComparisonList.SelectedItem = newest;
    }

    private void ComparisonList_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (ComparisonList.SelectedItem is not WireVisualComparison comparison)
        {
            ClearComparisonPreview();
            return;
        }
        ComparisonPreviewEmpty.Visibility = Visibility.Collapsed;
        ComparisonPreviewPanel.Visibility = Visibility.Visible;
        ComparisonPreviewTitle.Text = $"{comparison.State} / {comparison.Label} | {comparison.ImageStatus} {comparison.Score:P1}";
        WireMedianImage.Source = CreatePreview(comparison.MedianPreview);
        WireReliabilityImage.Source = CreatePreview(comparison.ReliabilityPreview);
        WireMatchedImage.Source = CreatePreview(comparison.MatchedPreview);
    }

    private void ClearComparisonPreview()
    {
        if (ComparisonPreviewPanel is null) return;
        ComparisonPreviewPanel.Visibility = Visibility.Collapsed;
        ComparisonPreviewEmpty.Visibility = Visibility.Visible;
        WireMedianImage.Source = null;
        WireReliabilityImage.Source = null;
        WireMatchedImage.Source = null;
    }

    private static ImageSource CreatePreview(GrayImage image)
    {
        BitmapSource bitmap = BitmapSource.Create(
            image.Width, image.Height, 96, 96, PixelFormats.Gray8, null,
            image.Pixels.ToArray(), image.Width);
        bitmap.Freeze();
        return bitmap;
    }

    private StoryWireTestOptions ReadOptions()
    {
        DebugEvidenceMode mode = WireModeBox.SelectedIndex == 0
            ? DebugEvidenceMode.Ocr
            : DebugEvidenceMode.ImageWithOcrFallback;
        int team = int.Parse(((ComboBoxItem)TeamBox.SelectedItem).Content.ToString()!);
        WireGameMode gameMode = Enum.Parse<WireGameMode>(
            ((ComboBoxItem)GameModeBox.SelectedItem).Tag.ToString()!);
        string map = gameMode == WireGameMode.Challenge
            ? "AUTO"
            : ((ComboBoxItem)MapBox.SelectedItem).Content.ToString()!;
        StoryAct act = Enum.Parse<StoryAct>(((ComboBoxItem)ActBox.SelectedItem).Tag.ToString()!);
        StoryDifficulty difficulty = Enum.Parse<StoryDifficulty>(
            ((ComboBoxItem)DifficultyBox.SelectedItem).Content.ToString()!);
        string key = UnitsKeyBox.SelectedItem?.ToString() ?? "H";
        return new StoryWireTestOptions(
            mode,
            gameMode,
            team,
            map,
            act,
            difficulty,
            EnabledChallengeTypes(),
            new StoryWireNavigationKeys(PlayMenu: null, UnitInventory: key[0], AreasMenu: null),
            new PlacementRuntimeKeys(),
            KeyboardKey.LeftControl,
            _device,
            RunRuntimeToggle.IsChecked == true,
            RepeatStageToggle.IsChecked == true);
    }

    private void ResetStages()
    {
        foreach (StoryWireStageItem item in _stages)
        {
            item.Status = StoryWireStageStatus.Waiting;
            item.Detail = string.Empty;
        }
    }

    private RegularChallengeType[] EnabledChallengeTypes()
    {
        if (SelectedGameMode() != WireGameMode.Challenge) return [];
        List<RegularChallengeType> types = [];
        if (TraitTypeToggle.IsChecked == true) types.Add(RegularChallengeType.Trait);
        if (StatTypeToggle.IsChecked == true) types.Add(RegularChallengeType.Stat);
        if (SpriteTypeToggle.IsChecked == true) types.Add(RegularChallengeType.Sprite);
        return [.. types];
    }

    private void AddEvent(string entry)
    {
        _events.Insert(0, $"{DateTime.Now:HH:mm:ss}  {entry}");
        while (_events.Count > 100) _events.RemoveAt(_events.Count - 1);
    }

    private void SetStatus(string text, string brush)
    {
        StatusText.Text = text;
        StatusBand.SetResourceReference(Border.BackgroundProperty, brush);
    }

    private void UpdateControls()
    {
        bool ready = _ocr.IsDeviceReady(_device);
        RunButton.IsEnabled = ready && !IsRunning;
        StopButton.IsEnabled = IsRunning;
        SetupButton.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        SetupButton.IsEnabled = !IsRunning;
        DeviceButton.IsEnabled = !IsRunning;
        KeepLoadedToggle.IsEnabled = _ocr.IsInstalled && !IsRunning;
        WireModeBox.IsEnabled = !IsRunning;
        GameModeBox.IsEnabled = !IsRunning;
        TeamBox.IsEnabled = !IsRunning;
        MapBox.IsEnabled = !IsRunning;
        ActBox.IsEnabled = !IsRunning;
        DifficultyBox.IsEnabled = !IsRunning && UsesDifficulty();
        UnitsKeyBox.IsEnabled = !IsRunning;
        RunRuntimeToggle.IsEnabled = !IsRunning;
        RepeatStageToggle.IsEnabled = !IsRunning && RunRuntimeToggle.IsChecked == true;
        MapFieldsPanel.Visibility = SelectedGameMode() == WireGameMode.Challenge
            ? Visibility.Collapsed
            : Visibility.Visible;
        ChallengeTypesPanel.Visibility = SelectedGameMode() == WireGameMode.Challenge
            ? Visibility.Visible
            : Visibility.Collapsed;
        TraitTypeToggle.IsEnabled = !IsRunning;
        StatTypeToggle.IsEnabled = !IsRunning;
        SpriteTypeToggle.IsEnabled = !IsRunning;
        DeviceButton.Content = _device == OcrRunner.GpuDevice ? "GPU" : "CPU";
        SetupButton.Content = _device == OcrRunner.GpuDevice ? "SET UP GPU" : "SET UP OCR";
    }

    private bool UsesDifficulty() => ActBox.SelectedItem is ComboBoxItem item &&
        SelectedGameMode() == WireGameMode.Story && item.Tag?.ToString() is not ("Infinite" or "Mastery");

    private void ActBox_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (DifficultyBox is not null) DifficultyBox.IsEnabled = !IsRunning && UsesDifficulty();
    }

    private void GameModeBox_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (MapBox is null || ActBox is null) return;
        UpdateModeChoices();
        RefreshStages();
        UpdateControls();
    }

    private void UpdateModeChoices()
    {
        WireGameMode mode = SelectedGameMode();
        MapBox.Items.Clear();
        if (mode == WireGameMode.Raid)
        {
            MapBox.Items.Add(new ComboBoxItem { Content = "Spirit City" });
            while (ActBox.Items.Count > 3) ActBox.Items.RemoveAt(ActBox.Items.Count - 1);
        }
        else if (mode == WireGameMode.Story)
        {
            foreach (string map in new[] { "School Grounds", "Flower Forest", "Rose Kingdom", "Fairy King Forest", "King's Tomb", "East Town" })
                MapBox.Items.Add(new ComboBoxItem { Content = map });
            string[] tags = ["Act4", "Act5", "Infinite", "Mastery"];
            string[] labels = ["Act 4", "Act 5", "Infinite", "Mastery"];
            for (int index = ActBox.Items.Count; index < 7; index++)
                ActBox.Items.Add(new ComboBoxItem { Content = labels[index - 3], Tag = tags[index - 3] });
        }
        else if (mode == WireGameMode.Event)
        {
            MapBox.Items.Add(new ComboBoxItem { Content = "Villain Invasion" });
            while (ActBox.Items.Count > 4) ActBox.Items.RemoveAt(ActBox.Items.Count - 1);
            if (ActBox.Items.Count < 4)
                ActBox.Items.Add(new ComboBoxItem { Content = "Act 4", Tag = "Act4" });
        }
        else
        {
            MapBox.Items.Add(new ComboBoxItem { Content = "AUTO" });
        }
        MapBox.SelectedIndex = 0;
        ActBox.SelectedIndex = Math.Max(0, Math.Min(ActBox.SelectedIndex, ActBox.Items.Count - 1));
    }

    private WireGameMode SelectedGameMode() => GameModeBox.SelectedItem is ComboBoxItem item &&
        Enum.TryParse(item.Tag?.ToString(), out WireGameMode mode)
            ? mode
            : WireGameMode.Story;

    private void RefreshStages()
    {
        if (_stages is null || GameModeBox is null) return;
        StoryWireStage[] middle = SelectedGameMode() == WireGameMode.Challenge
            ? [StoryWireStage.ChallengeType, StoryWireStage.ChallengeState]
            : [StoryWireStage.StoryMap, StoryWireStage.StoryAct];
        StoryWireStage[] stages =
        [
            StoryWireStage.Startup,
            StoryWireStage.Lobby,
            StoryWireStage.Units,
            StoryWireStage.Teams,
            StoryWireStage.LoadTeam,
            StoryWireStage.Play,
            .. middle,
            StoryWireStage.MatchPreview,
            StoryWireStage.MatchPrestart,
            StoryWireStage.MatchRuntime,
        ];
        _stages.Clear();
        for (int index = 0; index < stages.Length; index++)
            _stages.Add(new StoryWireStageItem(index + 1, stages[index]));
    }

    private void RunRuntime_OnChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (RepeatStageToggle is not null) RepeatStageToggle.IsEnabled = !IsRunning && RunRuntimeToggle.IsChecked == true;
    }

    private void DeviceButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (IsRunning) return;
        _device = _device == OcrRunner.CpuDevice ? OcrRunner.GpuDevice : OcrRunner.CpuDevice;
        UpdateControls();
    }

    private void KeepLoaded_OnChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (!IsLoaded || IsRunning) return;
        _ocr.KeepLoaded = KeepLoadedToggle.IsChecked == true;
    }

    private async void SetupButton_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (IsRunning) return;
        SetupButton.IsEnabled = false;
        SetStatus("SETTING UP", "YellowBrush");
        try
        {
            await _ocr.SetupAsync(_device);
            SetStatus("READY", "SuccessBrush");
        }
        catch (Exception error)
        {
            SetStatus("ERROR", "DangerBrush");
            AddEvent($"ERROR {error.Message}");
        }
        finally
        {
            UpdateControls();
        }
    }
}
