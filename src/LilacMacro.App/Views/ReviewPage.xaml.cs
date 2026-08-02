using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;

namespace LilacMacro.App.Views;

public partial class ReviewPage : UserControl, IWorkspacePage
{
    private readonly WorkspaceController _workspace;
    private readonly OcrRunner _ocr;
    private readonly DispatcherTimer _autosaveTimer;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private DatasetFrame? _activeFrame;
    private Guid? _selectedAnnotationId;
    private bool _binding;
    private bool _pendingSave;
    private bool _ocrBusy;

    public ReviewPage(WorkspaceController workspace, OcrRunner ocr)
    {
        _workspace = workspace;
        _ocr = ocr;
        InitializeComponent();
        KeepOcrLoadedToggle.IsChecked = _ocr.KeepLoaded;
        VerdictCombo.ItemsSource = Enum.GetValues<FrameVerdict>();
        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _autosaveTimer.Tick += AutosaveTimer_OnTick;
    }

    public async Task RefreshAsync()
    {
        if (_workspace.ActiveDataset is null && _workspace.RecentDataset is { } recent)
        {
            await _workspace.OpenDatasetAsync(recent.DirectoryPath);
        }

        BindDataset();
    }

    public async Task FlushPendingAsync()
    {
        _autosaveTimer.Stop();
        await _saveGate.WaitAsync();
        try
        {
            if (!_pendingSave || _workspace.ActiveDataset is null) return;
            SaveStatusText.Text = "SAVING";
            try
            {
                await _workspace.SaveActiveDatasetAsync();
                _pendingSave = false;
                SaveStatusText.Text = "SAVED";
            }
            catch (Exception error)
            {
                SaveStatusText.Text = "SAVE FAILED";
                OcrStatusText.Text = error.Message;
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void BindDataset()
    {
        DatasetLocation? dataset = _workspace.ActiveDataset;
        EmptyState.Visibility = dataset is null ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceGrid.Visibility = dataset is null ? Visibility.Collapsed : Visibility.Visible;
        if (dataset is null)
        {
            DatasetPathText.Text = "No dataset open";
            SaveStatusText.Text = "NO DATASET";
            return;
        }

        string? previousFile = _activeFrame?.FileName;
        _binding = true;
        DatasetPathText.Text = dataset.DirectoryPath;
        DatasetNameText.Text = dataset.Manifest.Name;
        DatasetNotesText.Text = dataset.Manifest.Notes;
        DatasetNameText.IsEnabled = !dataset.Manifest.IsFinalized;
        FinalizeButton.IsEnabled = !dataset.Manifest.IsFinalized;
        FinalizeButton.Content = dataset.Manifest.IsFinalized ? "DATASET FINALIZED" : "FINALIZE DATASET";
        FrameList.ItemsSource = dataset.Manifest.Frames.Select((frame, index) =>
            new FrameListItem(frame, index, ReviewImages.LoadThumbnail(Path.Combine(dataset.ImagesPath, frame.FileName)))).ToArray();
        int selectedIndex = dataset.Manifest.Frames.FindIndex(frame => frame.FileName == previousFile);
        FrameList.SelectedIndex = selectedIndex >= 0 ? selectedIndex : (dataset.Manifest.Frames.Count > 0 ? 0 : -1);
        _binding = false;
        SaveStatusText.Text = dataset.Manifest.IsFinalized ? "FINALIZED" : "DRAFT SAVED";
        SetActiveFrame((FrameList.SelectedItem as FrameListItem)?.Frame);
    }

    private void SetActiveFrame(DatasetFrame? frame)
    {
        _activeFrame = frame;
        if (frame is null)
        {
            _selectedAnnotationId = null;
            AnnotationCanvas.Clear();
            OcrMap.Clear();
            BindInspector();
            return;
        }

        if (_selectedAnnotationId is not { } id || frame.Annotations.All(annotation => annotation.Id != id))
        {
            _selectedAnnotationId = frame.Annotations.FirstOrDefault()?.Id;
        }
        RenderSurfaces();
        BindInspector();
    }

    private void RenderSurfaces()
    {
        DatasetLocation? dataset = _workspace.ActiveDataset;
        if (dataset is null || _activeFrame is null) return;
        string imagePath = Path.Combine(dataset.ImagesPath, _activeFrame.FileName);
        AnnotationCanvas.ShowFrame(imagePath, _activeFrame, _selectedAnnotationId);
        OcrMap.ShowFrame(imagePath, _activeFrame, _selectedAnnotationId, SelectedMapModel, SelectedOcrDevice);
    }

    private void BindInspector()
    {
        _binding = true;
        VerdictCombo.SelectedItem = _activeFrame?.Verdict ?? FrameVerdict.Unreviewed;
        FrameNotesText.Text = _activeFrame?.Notes ?? string.Empty;
        VerdictCombo.IsEnabled = _activeFrame is not null;
        FrameNotesText.IsEnabled = _activeFrame is not null;

        BoxAnnotation? annotation = CurrentAnnotation;
        NoRegionText.Visibility = annotation is null ? Visibility.Visible : Visibility.Collapsed;
        RegionPanel.Visibility = annotation is null ? Visibility.Collapsed : Visibility.Visible;
        if (annotation is not null)
        {
            PixelRect box = annotation.Bounds;
            CoordinatesText.Text = $"x={box.X}  y={box.Y}\nw={box.Width}  h={box.Height}";
            EdgesText.Text = $"right={box.Right}  bottom={box.Bottom}  (exclusive)";
            RegionLabelText.Text = annotation.Label;
            RegionNotesText.Text = annotation.Notes;
        }
        _binding = false;
        RenderOcrResults();
        UpdateOcrControls();
    }

    private void RenderOcrResults()
    {
        if (OcrResults is null) return;
        BoxAnnotation? annotation = CurrentAnnotation;
        OcrTrial[] latest = ReviewOcrSupport.Latest(annotation);
        OcrResults.ItemsSource = ReviewOcrSupport.Present(latest);
        OcrStatusText.Text = latest.Length == 0
            ? "NO RESULTS"
            : $"{latest.Length} RUN{(latest.Length == 1 ? string.Empty : "S")}";
        UseOcrTextButton.IsEnabled = latest.Length > 0 && !_ocrBusy;
    }

    private void UpdateOcrControls()
    {
        bool hasRegion = CurrentAnnotation is not null;
        string device = SelectedOcrDevice;
        bool deviceReady = _ocr.IsDeviceReady(device);
        OcrInstallText.Text = deviceReady
            ? $"READY · {(device == OcrRunner.GpuDevice ? "GPU" : "CPU")}"
            : device == OcrRunner.GpuDevice ? "GPU NOT SET UP" : "NOT SET UP";
        SetupOcrButton.Content = device == OcrRunner.GpuDevice ? "SET UP GPU" : "SET UP OCR RUNTIME";
        SetupOcrButton.Visibility = deviceReady ? Visibility.Collapsed : Visibility.Visible;
        RunOcrButton.IsEnabled = hasRegion && deviceReady && !_ocrBusy;
        CompareOcrButton.IsEnabled = hasRegion && deviceReady && !_ocrBusy;
        OcrModelCombo.IsEnabled = !_ocrBusy;
        OcrDeviceCombo.IsEnabled = !_ocrBusy;
        KeepOcrLoadedToggle.IsEnabled = _ocr.IsInstalled && !_ocrBusy;
    }

    private void MarkDirty()
    {
        if (_binding || _workspace.ActiveDataset is null) return;
        _pendingSave = true;
        SaveStatusText.Text = "UNSAVED EDITS";
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
    }

    private async void AutosaveTimer_OnTick(object? sender, EventArgs eventArgs) => await FlushPendingAsync();

    private void FrameList_OnSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (_binding) return;
        SetActiveFrame((FrameList.SelectedItem as FrameListItem)?.Frame);
    }

    private void AnnotationCanvas_OnRegionCreated(object? sender, PixelRect bounds)
    {
        if (_activeFrame is null) return;
        BoxAnnotation annotation = new() { Bounds = bounds };
        _activeFrame.Annotations.Add(annotation);
        _selectedAnnotationId = annotation.Id;
        FrameList.Items.Refresh();
        MarkDirty();
        RenderSurfaces();
        BindInspector();
        RegionLabelText.Focus();
    }

    private void AnnotationCanvas_OnRegionSelected(object? sender, Guid id)
    {
        _selectedAnnotationId = id;
        RenderSurfaces();
        BindInspector();
    }

    private void DatasetField_OnTextChanged(object sender, TextChangedEventArgs eventArgs)
    {
        if (_binding || _workspace.ActiveDataset is not { } dataset) return;
        dataset.Manifest.Name = DatasetNameText.Text;
        dataset.Manifest.Notes = DatasetNotesText.Text;
        MarkDirty();
    }

    private void FrameNotes_OnTextChanged(object sender, TextChangedEventArgs eventArgs)
    {
        if (_binding || _activeFrame is null) return;
        _activeFrame.Notes = FrameNotesText.Text;
        MarkDirty();
    }

    private void Verdict_OnChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (_binding || _activeFrame is null || VerdictCombo.SelectedItem is not FrameVerdict verdict) return;
        _activeFrame.Verdict = verdict;
        FrameList.Items.Refresh();
        MarkDirty();
    }

    private void RegionField_OnTextChanged(object sender, TextChangedEventArgs eventArgs)
    {
        if (_binding || CurrentAnnotation is not { } annotation) return;
        annotation.Label = RegionLabelText.Text;
        annotation.Notes = RegionNotesText.Text;
        AnnotationCanvas.Select(annotation.Id);
        MarkDirty();
    }

    private void DeleteRegion_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_activeFrame is null || CurrentAnnotation is not { } annotation) return;
        _activeFrame.Annotations.Remove(annotation);
        _selectedAnnotationId = _activeFrame.Annotations.FirstOrDefault()?.Id;
        FrameList.Items.Refresh();
        MarkDirty();
        RenderSurfaces();
        BindInspector();
    }

    private async void Finalize_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            await FlushPendingAsync();
            await _workspace.FinalizeActiveDatasetAsync(DatasetNameText.Text, DatasetNotesText.Text);
            BindDataset();
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Finalize dataset", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void SetupOcr_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            _ocrBusy = true;
            OcrInstallText.Text = "Installing the isolated OCR runtime…";
            UpdateOcrControls();
            await _ocr.SetupAsync(SelectedOcrDevice);
            OcrInstallText.Text = "OCR runtime ready.";
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "OCR setup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _ocrBusy = false;
            UpdateOcrControls();
        }
    }

    private async void RunOcr_OnClick(object sender, RoutedEventArgs eventArgs) => await RunOcrAsync(SelectedOcrModel);

    private async void CompareOcr_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        await RunOcrAsync(OcrRunner.SmallModel);
        await RunOcrAsync(OcrRunner.TinyModel);
        ShowOcrMap();
    }

    private async Task RunOcrAsync(string model)
    {
        DatasetLocation? dataset = _workspace.ActiveDataset;
        DatasetFrame? frame = _activeFrame;
        BoxAnnotation? annotation = CurrentAnnotation;
        if (dataset is null || frame is null || annotation is null || _ocrBusy) return;
        try
        {
            _ocrBusy = true;
            UpdateOcrControls();
            string imagePath = Path.Combine(dataset.ImagesPath, frame.FileName);
            string device = SelectedOcrDevice;
            OcrStatusText.Text = $"Running {model} · {device} on [{annotation.Bounds.X},{annotation.Bounds.Y},{annotation.Bounds.Width},{annotation.Bounds.Height}]…";
            OcrWorkerResult result = await _ocr.RunAsync(imagePath, annotation.Bounds, model, device);
            annotation.OcrTrials.Add(ReviewOcrSupport.CreateTrial(result));
            MarkDirty();
            await FlushPendingAsync();
            RenderSurfaces();
            RenderOcrResults();
        }
        catch (Exception error)
        {
            OcrStatusText.Text = error.Message;
        }
        finally
        {
            _ocrBusy = false;
            UpdateOcrControls();
            RenderOcrResults();
        }
    }

    private void UseOcrText_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        OcrTrial? trial = ReviewOcrSupport.Latest(CurrentAnnotation, SelectedOcrModel, SelectedOcrDevice);
        if (trial is not null) RegionLabelText.Text = trial.Text;
    }

    private void AnnotateView_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        AnnotationCanvas.Visibility = Visibility.Visible;
        OcrMap.Visibility = Visibility.Collapsed;
        MapOnlyToggle.Visibility = Visibility.Collapsed;
        AnnotateViewButton.Background = (Brush)FindResource("AccentBrush");
        OcrMapViewButton.Background = (Brush)FindResource("CardBrush");
        ZoomControls.IsEnabled = true;
        ZoomText.Text = $"{AnnotationCanvas.Zoom:P0}";
    }

    private void OcrMapView_OnClick(object sender, RoutedEventArgs eventArgs) => ShowOcrMap();

    private void ShowOcrMap()
    {
        RenderSurfaces();
        AnnotationCanvas.Visibility = Visibility.Collapsed;
        OcrMap.Visibility = Visibility.Visible;
        MapOnlyToggle.Visibility = Visibility.Visible;
        AnnotateViewButton.Background = (Brush)FindResource("CardBrush");
        OcrMapViewButton.Background = (Brush)FindResource("AccentBrush");
        ZoomControls.IsEnabled = true;
        ZoomText.Text = $"{OcrMap.Zoom:P0}";
    }

    private void MapOnly_OnChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (!IsLoaded) return;
        OcrMap.SetTextOnly(MapOnlyToggle.IsChecked == true);
    }

    private void OcrModel_OnChanged(object sender, SelectionChangedEventArgs eventArgs) => RenderOcrResults();

    private void OcrDevice_OnChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (!IsLoaded) return;
        RenderOcrResults();
        UpdateOcrControls();
        RenderSurfaces();
    }

    private void MapModel_OnChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (IsLoaded) RenderSurfaces();
    }

    private void KeepOcrLoaded_OnChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (_ocrBusy) return;
        _ocr.KeepLoaded = KeepOcrLoadedToggle.IsChecked == true;
    }

    private void ZoomOut_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (OcrMap.Visibility == Visibility.Visible) OcrMap.ZoomOut();
        else AnnotationCanvas.ZoomOut();
    }

    private void ZoomIn_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (OcrMap.Visibility == Visibility.Visible) OcrMap.ZoomIn();
        else AnnotationCanvas.ZoomIn();
    }

    private void ZoomFit_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (OcrMap.Visibility == Visibility.Visible) OcrMap.FitToViewport();
        else AnnotationCanvas.FitToViewport();
    }

    private void AnnotationCanvas_OnZoomChanged(object? sender, EventArgs eventArgs)
    {
        if (AnnotationCanvas.Visibility == Visibility.Visible) ZoomText.Text = $"{AnnotationCanvas.Zoom:P0}";
    }

    private void OcrMap_OnZoomChanged(object? sender, EventArgs eventArgs)
    {
        if (OcrMap.Visibility == Visibility.Visible) ZoomText.Text = $"{OcrMap.Zoom:P0}";
    }

    private BoxAnnotation? CurrentAnnotation => _activeFrame?.Annotations
        .FirstOrDefault(annotation => annotation.Id == _selectedAnnotationId);

    private string SelectedOcrModel => SelectedModel(OcrModelCombo, OcrRunner.SmallModel);

    private string SelectedOcrDevice => SelectedModel(OcrDeviceCombo, OcrRunner.CpuDevice);

    private string SelectedMapModel => SelectedModel(MapModelCombo, OcrRunner.SmallModel);

    private static string SelectedModel(ComboBox combo, string fallback) =>
        combo.SelectedItem is ComboBoxItem { Tag: string model } ? model : fallback;

}
