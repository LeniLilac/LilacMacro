using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Datasets;

namespace LilacMacro.App.Views;

public partial class CapturePage : UserControl, IWorkspacePage
{
    private readonly WorkspaceController _workspace;
    private readonly Func<PageKind, Task> _navigate;
    private CancellationTokenSource? _captureCancellation;

    public event EventHandler? CaptureStateChanged;

    public CaptureRunState CaptureState { get; private set; }

    public bool IsCapturing => CaptureState == CaptureRunState.Capturing;

    public CapturePage(WorkspaceController workspace, Func<PageKind, Task> navigate)
    {
        InitializeComponent();
        _workspace = workspace;
        _navigate = navigate;
    }

    public async Task RefreshAsync()
    {
        await _workspace.RefreshWindowAsync();
        WidthText.Text = _workspace.TargetSize.Width.ToString(CultureInfo.InvariantCulture);
        HeightText.Text = _workspace.TargetSize.Height.ToString(CultureInfo.InvariantCulture);
        FrameCountText.Text = _workspace.FrameCount.ToString(CultureInfo.InvariantCulture);
        DurationText.Text = _workspace.DurationSeconds.ToString("0.##", CultureInfo.InvariantCulture);
        DatasetRootText.Text = _workspace.DatasetRoot;
        RenderWindowStatus();
    }

    public Task<bool> CaptureFromHotkeyAsync() => RunCaptureAsync(navigateAfter: false, showErrors: false);

    private async Task SaveSettingsAsync()
    {
        if (!int.TryParse(WidthText.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int width) ||
            !int.TryParse(HeightText.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int height) ||
            !int.TryParse(FrameCountText.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int frameCount) ||
            !double.TryParse(DurationText.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double duration))
        {
            throw new InvalidOperationException("Width, height, images, and seconds must be valid numbers.");
        }
        await _workspace.UpdateSettingsAsync(width, height, frameCount, duration, DatasetRootText.Text);
    }

    private void RenderWindowStatus()
    {
        ObservedText.Text = _workspace.RobloxWindow is null
            ? $"ROBLOX OFFLINE  |  target {_workspace.TargetSize}"
            : $"OBSERVED {_workspace.ObservedClientSize}  |  target {_workspace.TargetSize}";
        RunHeadline.Text = _workspace.WindowIsReady
            ? $"READY · {_workspace.TargetSize}"
            : "SIZE REQUIRED";
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

    private async void Start_OnClick(object sender, RoutedEventArgs eventArgs) =>
        await RunCaptureAsync(navigateAfter: true, showErrors: true);

    private async Task<bool> RunCaptureAsync(bool navigateAfter, bool showErrors)
    {
        if (IsCapturing) return false;
        CaptureState = CaptureRunState.Capturing;
        CaptureStateChanged?.Invoke(this, EventArgs.Empty);
        SetRunning(true);
        try
        {
            await SaveSettingsAsync();
            _captureCancellation = new CancellationTokenSource();
            Progress<CaptureProgress> progress = new(UpdateProgress);
            DatasetLocation dataset = await _workspace.CaptureDatasetAsync(progress, _captureCancellation.Token);
            CaptureState = CaptureRunState.Complete;
            RunHeadline.Text = "CAPTURE COMPLETE";
            RunDetail.Text = dataset.DirectoryPath;
            if (navigateAfter) await _navigate(PageKind.Review);
            return true;
        }
        catch (OperationCanceledException)
        {
            CaptureState = CaptureRunState.Cancelled;
            RunHeadline.Text = "CAPTURE CANCELLED";
            RunDetail.Text = _workspace.ActiveDataset?.DirectoryPath ?? string.Empty;
            return false;
        }
        catch (Exception error)
        {
            CaptureState = CaptureRunState.Failed;
            RunHeadline.Text = "CAPTURE FAILED";
            RunDetail.Text = error.Message;
            if (showErrors) MessageBox.Show(error.Message, "Dataset capture", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            _captureCancellation?.Dispose();
            _captureCancellation = null;
            SetRunning(false);
            CaptureStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateProgress(CaptureProgress progress)
    {
        CaptureProgressBar.Maximum = Math.Max(1, progress.Total);
        CaptureProgressBar.Value = progress.Completed;
        ProgressText.Text = $"{progress.Completed} / {progress.Total}";
        RunDetail.Text = progress.Message;
    }

    private void SetRunning(bool running)
    {
        StartButton.IsEnabled = !running;
        CancelButton.IsEnabled = running;
        WidthText.IsEnabled = !running;
        HeightText.IsEnabled = !running;
        FrameCountText.IsEnabled = !running;
        DurationText.IsEnabled = !running;
        DatasetRootText.IsEnabled = !running;
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

    private void Cancel_OnClick(object sender, RoutedEventArgs eventArgs) => _captureCancellation?.Cancel();
}
