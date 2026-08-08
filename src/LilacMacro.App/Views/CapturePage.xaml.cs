using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using LilacMacro.App.Workspace;
using LilacMacro.App.Diagnostics;
using LilacMacro.Core.Datasets;

namespace LilacMacro.App.Views;

public partial class CapturePage : UserControl, IWorkspacePage
{
    private readonly WorkspaceController _workspace;
    private readonly Func<PageKind, Task> _navigate;
    private CancellationTokenSource? _captureCancellation;
    private bool _binding;
    private readonly DeepDebugSessionService _deepDebug;
    private DeepDebugScope? _manualDebugScope;

    public CapturePage(
        WorkspaceController workspace,
        Func<PageKind, Task> navigate,
        DeepDebugSessionService deepDebug)
    {
        _workspace = workspace;
        _navigate = navigate;
        _deepDebug = deepDebug;
        InitializeComponent();
    }

    public event EventHandler? CaptureStateChanged;

    public CaptureRunState TimedCaptureState { get; private set; }

    public CaptureRunState ManualCaptureState { get; private set; }

    public bool IsCapturing => TimedCaptureState == CaptureRunState.Capturing ||
        ManualCaptureState == CaptureRunState.Capturing;

    public bool IsManualSessionActive => _workspace.IsManualCaptureActive;

    public bool CanCaptureManualFrame => IsManualSessionActive && !IsCapturing;

    public async Task RefreshAsync()
    {
        await _workspace.RefreshWindowAsync();
        _binding = true;
        WidthText.Text = _workspace.TargetSize.Width.ToString(CultureInfo.InvariantCulture);
        HeightText.Text = _workspace.TargetSize.Height.ToString(CultureInfo.InvariantCulture);
        FrameCountText.Text = _workspace.FrameCount.ToString(CultureInfo.InvariantCulture);
        DurationText.Text = _workspace.DurationSeconds.ToString("0.##", CultureInfo.InvariantCulture);
        DatasetRootText.Text = _workspace.DatasetRoot;
        SelectCaptureMode(_workspace.CaptureMode);
        _binding = false;
        RenderWindowStatus();
        UpdateControls();
    }

    public Task<bool> CaptureFromHotkeyAsync() => RunTimedCaptureAsync(navigateAfter: false, showErrors: false);

    public Task<bool> CaptureManualFrameFromHotkeyAsync() => CaptureManualFrameAsync(showErrors: false);

    public async Task CompleteForCloseAsync()
    {
        _captureCancellation?.Cancel();
        if (_manualDebugScope is not null) await _manualDebugScope.CompleteAsync("closed");
        _manualDebugScope = null;
    }

    private async Task SaveSettingsAsync()
    {
        if (!int.TryParse(WidthText.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int width) ||
            !int.TryParse(HeightText.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int height))
        {
            throw new InvalidOperationException("Width and height must be valid numbers.");
        }

        DatasetCaptureMode mode = SelectedCaptureMode;
        int frameCount = _workspace.FrameCount;
        double duration = _workspace.DurationSeconds;
        if (mode == DatasetCaptureMode.Timed &&
            (!int.TryParse(FrameCountText.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out frameCount) ||
             !double.TryParse(DurationText.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out duration)))
        {
            throw new InvalidOperationException("Images and seconds must be valid numbers.");
        }
        await _workspace.UpdateSettingsAsync(width, height, frameCount, duration, mode, DatasetRootText.Text);
    }

    private void RenderWindowStatus()
    {
        ObservedText.Text = _workspace.RobloxWindow is null
            ? $"ROBLOX OFFLINE  |  target {_workspace.TargetSize}"
            : $"OBSERVED {_workspace.ObservedClientSize}  |  target {_workspace.TargetSize}";
        if (IsManualSessionActive)
        {
            RunHeadline.Text = "MANUAL READY";
            RunDetail.Text = _workspace.ActiveDataset?.DirectoryPath ?? string.Empty;
            UpdateManualProgress();
            return;
        }
        RunHeadline.Text = _workspace.WindowIsReady
            ? SelectedCaptureMode == DatasetCaptureMode.Manual ? "READY · MANUAL" : $"READY · {_workspace.TargetSize}"
            : "SIZE REQUIRED";
        RunDetail.Text = string.Empty;
        if (SelectedCaptureMode == DatasetCaptureMode.Timed)
        {
            CaptureProgressBar.Maximum = Math.Max(1, _workspace.FrameCount);
            CaptureProgressBar.Value = 0;
            ProgressText.Text = $"0 / {_workspace.FrameCount}";
        }
        else
        {
            ProgressText.Text = "0 FRAMES";
        }
    }

    private async void ApplySize_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            await SaveSettingsAsync();
            await _workspace.RefreshWindowAsync();
            await _workspace.ApplyTargetSizeAsync();
            RenderWindowStatus();
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Roblox sizing", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Start_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (SelectedCaptureMode == DatasetCaptureMode.Timed)
        {
            await RunTimedCaptureAsync(navigateAfter: true, showErrors: true);
        }
        else if (IsManualSessionActive)
        {
            await CaptureManualFrameAsync(showErrors: true);
        }
        else
        {
            await StartManualCaptureAsync(showErrors: true);
        }
    }

    private async Task<bool> RunTimedCaptureAsync(bool navigateAfter, bool showErrors)
    {
        if (IsCapturing || IsManualSessionActive) return false;
        TimedCaptureState = CaptureRunState.Capturing;
        BeginOperation();
        try
        {
            await SaveSettingsAsync();
            _captureCancellation = new CancellationTokenSource();
            Progress<CaptureProgress> progress = new(UpdateProgress);
            DatasetLocation dataset = await _deepDebug.RunOperationAsync(
                "timed-dataset-capture",
                new DeepDebugOperationContext("dataset-builder", new
                {
                    _workspace.TargetSize,
                    _workspace.FrameCount,
                    _workspace.DurationSeconds,
                    _workspace.DatasetRoot,
                }),
                token => _workspace.CaptureDatasetAsync(progress, token),
                _captureCancellation.Token);
            TimedCaptureState = CaptureRunState.Complete;
            RunHeadline.Text = "CAPTURE COMPLETE";
            RunDetail.Text = dataset.DirectoryPath;
            if (navigateAfter) await _navigate(PageKind.Review);
            return true;
        }
        catch (OperationCanceledException)
        {
            TimedCaptureState = CaptureRunState.Cancelled;
            RunHeadline.Text = "CAPTURE CANCELLED";
            RunDetail.Text = _workspace.ActiveDataset?.DirectoryPath ?? string.Empty;
            return false;
        }
        catch (Exception error)
        {
            TimedCaptureState = CaptureRunState.Failed;
            ShowCaptureError(error, showErrors);
            return false;
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task<bool> StartManualCaptureAsync(bool showErrors)
    {
        if (IsCapturing || IsManualSessionActive) return false;
        ManualCaptureState = CaptureRunState.Capturing;
        BeginOperation();
        try
        {
            await SaveSettingsAsync();
            _captureCancellation = new CancellationTokenSource();
            _manualDebugScope = await _deepDebug.OpenSessionAsync(
                "manual-dataset-capture",
                new DeepDebugOperationContext("dataset-builder", new
                {
                    _workspace.TargetSize,
                    _workspace.DatasetRoot,
                }));
            DatasetLocation dataset = await _workspace.StartManualCaptureAsync(_captureCancellation.Token);
            ManualCaptureState = CaptureRunState.Complete;
            RunHeadline.Text = "MANUAL READY";
            RunDetail.Text = dataset.DirectoryPath;
            UpdateManualProgress();
            return true;
        }
        catch (OperationCanceledException)
        {
            if (_manualDebugScope is not null) await _manualDebugScope.CompleteAsync("canceled");
            _manualDebugScope = null;
            ManualCaptureState = CaptureRunState.Cancelled;
            RunHeadline.Text = "MANUAL CANCELLED";
            return false;
        }
        catch (Exception error)
        {
            if (_manualDebugScope is not null) await _manualDebugScope.CompleteAsync("error", error);
            _manualDebugScope = null;
            ManualCaptureState = CaptureRunState.Failed;
            ShowCaptureError(error, showErrors);
            return false;
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task<bool> CaptureManualFrameAsync(bool showErrors)
    {
        if (!CanCaptureManualFrame) return false;
        ManualCaptureState = CaptureRunState.Capturing;
        BeginOperation();
        try
        {
            _captureCancellation = new CancellationTokenSource();
            int next = (_workspace.ActiveDataset?.Manifest.Frames.Count ?? 0) + 1;
            RunHeadline.Text = $"CAPTURING FRAME {next}";
            DatasetFrame frame = await _workspace.CaptureManualFrameAsync(_captureCancellation.Token);
            int count = _workspace.ActiveDataset?.Manifest.Frames.Count ?? 0;
            ManualCaptureState = CaptureRunState.Complete;
            RunHeadline.Text = $"FRAME {count} SAVED";
            RunDetail.Text = frame.FileName;
            UpdateManualProgress();
            return true;
        }
        catch (OperationCanceledException)
        {
            ManualCaptureState = CaptureRunState.Cancelled;
            RunHeadline.Text = "FRAME CANCELLED";
            return false;
        }
        catch (Exception error)
        {
            ManualCaptureState = CaptureRunState.Failed;
            ShowCaptureError(error, showErrors);
            return false;
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task FinishManualCaptureAsync()
    {
        DatasetLocation? dataset = _workspace.EndManualCapture();
        if (dataset is null) return;
        if (_manualDebugScope is not null) await _manualDebugScope.CompleteAsync("success");
        _manualDebugScope = null;
        ManualCaptureState = CaptureRunState.Complete;
        RunHeadline.Text = "MANUAL COMPLETE";
        RunDetail.Text = dataset.DirectoryPath;
        UpdateControls();
        CaptureStateChanged?.Invoke(this, EventArgs.Empty);
        await _navigate(PageKind.Review);
    }

    private void BeginOperation()
    {
        UpdateControls();
        CaptureStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EndOperation()
    {
        _captureCancellation?.Dispose();
        _captureCancellation = null;
        UpdateControls();
        CaptureStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowCaptureError(Exception error, bool showErrors)
    {
        RunHeadline.Text = "CAPTURE FAILED";
        RunDetail.Text = error.Message;
        if (showErrors) MessageBox.Show(error.Message, "Dataset capture", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void UpdateProgress(CaptureProgress progress)
    {
        CaptureProgressBar.Maximum = Math.Max(1, progress.Total);
        CaptureProgressBar.Value = progress.Completed;
        ProgressText.Text = $"{progress.Completed} / {progress.Total}";
        RunDetail.Text = progress.Message;
    }

    private void UpdateManualProgress()
    {
        int count = _workspace.ActiveDataset?.Manifest.Frames.Count ?? 0;
        ProgressText.Text = $"{count} FRAME{(count == 1 ? string.Empty : "S")}";
    }

    private void UpdateControls()
    {
        bool manual = SelectedCaptureMode == DatasetCaptureMode.Manual;
        bool locked = IsManualSessionActive;
        bool busy = IsCapturing;
        TimedCaptureFields.Visibility = manual ? Visibility.Collapsed : Visibility.Visible;
        CaptureProgressBar.Visibility = manual ? Visibility.Collapsed : Visibility.Visible;
        StartButton.Content = manual
            ? locked ? "CAPTURE FRAME · F5" : "START MANUAL CAPTURE"
            : "START TIMED CAPTURE";
        CancelButton.Content = busy ? "CANCEL" : locked ? "FINISH + REVIEW" : "CANCEL";
        StartButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy || locked;
        CaptureModeCombo.IsEnabled = !busy && !locked;
        WidthText.IsEnabled = !busy && !locked;
        HeightText.IsEnabled = !busy && !locked;
        PresetCombo.IsEnabled = !busy && !locked;
        FrameCountText.IsEnabled = !busy && !locked;
        DurationText.IsEnabled = !busy && !locked;
        DatasetRootText.IsEnabled = !busy && !locked;
        ApplySizeButton.IsEnabled = !busy && !locked;
    }

    private async void Cancel_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (IsCapturing) _captureCancellation?.Cancel();
        else if (IsManualSessionActive) await FinishManualCaptureAsync();
    }

    private void Browse_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "Choose the LilacMacro dataset root",
            InitialDirectory = Directory.Exists(DatasetRootText.Text) ? DatasetRootText.Text : null,
        };
        if (dialog.ShowDialog() == true) DatasetRootText.Text = dialog.FolderName;
    }

    private void Preset_OnChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (PresetCombo.SelectedItem is not ComboBoxItem { Tag: string value }) return;
        string[] parts = value.Split(',');
        if (parts.Length != 2) return;
        WidthText.Text = parts[0];
        HeightText.Text = parts[1];
    }

    private void CaptureMode_OnChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (_binding || !IsLoaded) return;
        RenderWindowStatus();
        UpdateControls();
    }

    private void SelectCaptureMode(DatasetCaptureMode mode)
    {
        foreach (ComboBoxItem item in CaptureModeCombo.Items)
        {
            if (item.Tag is string tag && string.Equals(tag, mode.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                CaptureModeCombo.SelectedItem = item;
                return;
            }
        }
    }

    private DatasetCaptureMode SelectedCaptureMode =>
        CaptureModeCombo.SelectedItem is ComboBoxItem { Tag: "manual" }
            ? DatasetCaptureMode.Manual
            : DatasetCaptureMode.Timed;
}
