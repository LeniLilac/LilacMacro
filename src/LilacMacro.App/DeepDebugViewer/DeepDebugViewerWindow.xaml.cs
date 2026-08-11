using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LilacMacro.App.Theming;
using Microsoft.Win32;

namespace LilacMacro.App.DeepDebugViewer;

public partial class DeepDebugViewerWindow : Window
{
    private readonly ObservableCollection<NearbyEventItem> _nearbyEvents = [];
    private DeepDebugArchive? _archive;
    private DeepDebugFrameCache? _frameCache;
    private CancellationTokenSource? _openCancellation;
    private CancellationTokenSource? _displayCancellation;
    private CancellationTokenSource? _playbackCancellation;
    private CancellationTokenSource? _prefetchCancellation;
    private int _currentFrameIndex;
    private bool _settingSlider;
    private bool _isPlaying;
    private double _playbackSpeed = 1;

    public DeepDebugViewerWindow()
    {
        InitializeComponent();
        EventList.ItemsSource = _nearbyEvents;
        Closed += (_, _) => DisposeArchive();
    }

    public async void OpenArchiveFromCommandLine(string path) => await OpenArchiveAsync(path);

    private async void OpenArchive_Click(object sender, RoutedEventArgs eventArgs)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Open Deep Debug archive",
            Filter = "Deep Debug ZIP (*.zip)|*.zip|All files (*.*)|*.*",
            CheckFileExists = true,
            InitialDirectory = DefaultDiagnosticsDirectory(),
        };
        if (dialog.ShowDialog(this) == true) await OpenArchiveAsync(dialog.FileName);
    }

    private async Task OpenArchiveAsync(string path)
    {
        StopPlayback();
        StopPrefetch();
        _displayCancellation?.Cancel();
        _openCancellation?.Cancel();
        _openCancellation?.Dispose();
        _openCancellation = new CancellationTokenSource();
        CancellationToken token = _openCancellation.Token;
        SetReady(false, "OPENING ARCHIVE");
        ShowPreviewMessage("OPENING ARCHIVE...");
        try
        {
            Progress<string> progress = new(message => StatusText.Text = message);
            DeepDebugArchive opened = await DeepDebugArchive.OpenAsync(path, progress, token);
            if (opened.Frames.Count == 0)
            {
                opened.Dispose();
                throw new InvalidDataException("Archive contains no PNG frame captures.");
            }
            _archive?.Dispose();
            _archive = opened;
            _frameCache = new DeepDebugFrameCache(opened);
            _currentFrameIndex = opened.Frames.FindIndex(frame => frame.EntryExists);
            if (_currentFrameIndex < 0) _currentFrameIndex = 0;
            ArchivePathText.Text = opened.Path;
            OperationText.Text = Friendly(opened.Manifest.Operation);
            OutcomeText.Text = Friendly(opened.Manifest.Outcome);
            RuntimeText.Text = FormatRuntime(opened.Manifest.Runtime);
            FrameCountText.Text = opened.Frames.Count.ToString("N0", CultureInfo.InvariantCulture);
            EventCountText.Text = $"{opened.Events.Count:N0} / {opened.Manifest.DeclaredInputEvents:N0}";
            TimelineSlider.Maximum = Math.Max(0, opened.Frames.Count - 1);
            string warning = opened.MalformedEventLines == 0
                ? $"INDEXED {opened.Frames.Count:N0} FRAMES"
                : $"INDEXED {opened.Frames.Count:N0} FRAMES · SKIPPED {opened.MalformedEventLines:N0} BAD EVENTS";
            SetReady(true, warning);
            await ShowFrameAsync(_currentFrameIndex);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            SetReady(_archive is not null, "OPEN CANCELED");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SetReady(_archive is not null, "ARCHIVE FAILED");
            ShowPreviewMessage(error.Message, error: true);
        }
    }

    private async Task ShowFrameAsync(int requestedIndex)
    {
        DeepDebugArchive? archive = _archive;
        DeepDebugFrameCache? cache = _frameCache;
        if (archive is null || cache is null || archive.Frames.Count == 0) return;
        int index = Math.Clamp(requestedIndex, 0, archive.Frames.Count - 1);
        _currentFrameIndex = index;
        DeepDebugFrameRecord frame = archive.Frames[index];
        UpdateFrameMetadata(archive, frame);
        StopPrefetch();
        _displayCancellation?.Cancel();
        _displayCancellation?.Dispose();
        _displayCancellation = new CancellationTokenSource();
        CancellationToken token = _displayCancellation.Token;
        try
        {
            ShowPreviewMessage("LOADING FRAME...");
            BitmapSource bitmap = await cache.GetAsync(index, token);
            if (token.IsCancellationRequested || !ReferenceEquals(archive, _archive) || index != _currentFrameIndex) return;
            FrameSurface.Width = bitmap.PixelWidth;
            FrameSurface.Height = bitmap.PixelHeight;
            FrameImage.Source = bitmap;
            PreviewMessageBorder.Visibility = Visibility.Collapsed;
            DrawInputMarkers(bitmap.PixelWidth, bitmap.PixelHeight);
            StatusText.Text = $"FRAME {index + 1:N0} · CACHE {FormatBytes(cache.CurrentBytes)} / 1 GB";
            StartPrefetch(index, _isPlaying ? 30 : 8);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException)
        {
            FrameImage.Source = null;
            InputMarkerCanvas.Children.Clear();
            ShowPreviewMessage(error.Message, error: true);
            StatusText.Text = $"FRAME {index + 1:N0} UNAVAILABLE";
        }
    }

    private void UpdateFrameMetadata(DeepDebugArchive archive, DeepDebugFrameRecord frame)
    {
        _settingSlider = true;
        TimelineSlider.Value = frame.Index;
        _settingSlider = false;
        FramePathText.Text = frame.Path;
        FramePositionText.Text = $"{frame.Index + 1:N0} / {archive.Frames.Count:N0}";
        TimestampText.Text = frame.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        _nearbyEvents.Clear();
        foreach (DeepDebugTimelineEvent item in archive.GetNearbyEvents(frame.Index))
        {
            _nearbyEvents.Add(new(
                item.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                $"{Friendly(item.Category)} · {Friendly(item.Action)}",
                string.IsNullOrWhiteSpace(item.Details) ? $"SEQUENCE {item.Sequence:N0}" : item.Details));
        }
        IReadOnlyList<DeepDebugInputMarker> markers = archive.GetInputMarkers(frame.Index);
        EventContextText.Text = $"SEQUENCE {frame.Sequence:N0} · {_nearbyEvents.Count:N0} RECORDS · {markers.Count:N0} INPUT MARKERS";
    }

    private void DrawInputMarkers(int pixelWidth, int pixelHeight)
    {
        InputMarkerCanvas.Children.Clear();
        if (InputMarkersToggle.IsChecked != true || _archive is null) return;
        foreach (DeepDebugInputMarker marker in _archive.GetInputMarkers(_currentFrameIndex))
        {
            if (marker.LocalX < 0 || marker.LocalY < 0 || marker.LocalX >= pixelWidth || marker.LocalY >= pixelHeight) continue;
            Grid badge = BuildMarker(marker);
            Canvas.SetLeft(badge, marker.LocalX - 13);
            Canvas.SetTop(badge, marker.LocalY - 13);
            InputMarkerCanvas.Children.Add(badge);
        }
    }

    private Grid BuildMarker(DeepDebugInputMarker marker)
    {
        Grid grid = new() { Width = 126, Height = 30 };
        Ellipse circle = new() { Width = 26, Height = 26, StrokeThickness = 3, HorizontalAlignment = HorizontalAlignment.Left };
        circle.SetResourceReference(Shape.FillProperty, "AccentBrush");
        circle.SetResourceReference(Shape.StrokeProperty, "InkBrush");
        TextBlock number = new()
        {
            Text = marker.Number.ToString(CultureInfo.InvariantCulture),
            Width = 26,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            FontWeight = FontWeights.Black,
        };
        number.SetResourceReference(TextBlock.ForegroundProperty, "OnAccentBrush");
        Border label = new()
        {
            Margin = new Thickness(30, 1, 0, 1),
            Padding = new Thickness(6, 2, 6, 2),
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(2),
            Child = new TextBlock
            {
                Text = marker.WheelDelta is int delta ? $"SCROLL {delta:+#;-#;0}" : "CLICK",
                FontWeight = FontWeights.Bold,
                FontSize = 10,
            },
        };
        label.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        label.SetResourceReference(Border.BorderBrushProperty, "InkBrush");
        grid.Children.Add(label);
        grid.Children.Add(circle);
        grid.Children.Add(number);
        return grid;
    }

    private async void TimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> eventArgs)
    {
        if (_settingSlider || _archive is null) return;
        StopPlayback();
        await ShowFrameAsync((int)Math.Round(eventArgs.NewValue));
    }

    private async void Previous_Click(object sender, RoutedEventArgs eventArgs)
    {
        StopPlayback();
        await ShowFrameAsync(_currentFrameIndex - 1);
    }

    private async void Next_Click(object sender, RoutedEventArgs eventArgs)
    {
        StopPlayback();
        await ShowFrameAsync(_currentFrameIndex + 1);
    }

    private void PlayPause_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_isPlaying) StopPlayback(); else StartPlayback();
    }

    private void StartPlayback()
    {
        if (_archive is null || _isPlaying) return;
        if (_currentFrameIndex >= _archive.Frames.Count - 1) _currentFrameIndex = 0;
        _isPlaying = true;
        PlayPauseIcon.Data = (Geometry)FindResource("Lucide.Pause");
        _playbackCancellation?.Dispose();
        _playbackCancellation = new CancellationTokenSource();
        _ = PlaybackLoopAsync(_playbackCancellation.Token);
    }

    private void StopPlayback()
    {
        _playbackCancellation?.Cancel();
        _isPlaying = false;
        if (PlayPauseIcon is not null) PlayPauseIcon.Data = (Geometry)FindResource("Lucide.Play");
    }

    private async Task PlaybackLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (_archive is { } archive && _currentFrameIndex < archive.Frames.Count - 1)
            {
                DeepDebugFrameRecord current = archive.Frames[_currentFrameIndex];
                DeepDebugFrameRecord next = archive.Frames[_currentFrameIndex + 1];
                double actual = (next.TimestampUtc - current.TimestampUtc).TotalMilliseconds;
                double delay = Math.Clamp((actual <= 0 ? 100 : actual) / _playbackSpeed, 20, 1600);
                await Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken);
                await ShowFrameAsync(_currentFrameIndex + 1);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally { if (!cancellationToken.IsCancellationRequested) StopPlayback(); }
    }

    private void SpeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (SpeedCombo.SelectedItem is ComboBoxItem { Tag: string tag } &&
            double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out double speed)) _playbackSpeed = speed;
    }

    private void InputMarkers_Changed(object sender, RoutedEventArgs eventArgs)
    {
        if (FrameImage.Source is BitmapSource bitmap) DrawInputMarkers(bitmap.PixelWidth, bitmap.PixelHeight);
    }

    private async void Window_KeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (Keyboard.FocusedElement is ComboBox) return;
        if (eventArgs.Key == Key.O && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            OpenArchive_Click(this, new RoutedEventArgs());
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Space && _archive is not null)
        {
            if (_isPlaying) StopPlayback(); else StartPlayback();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Left && _archive is not null)
        {
            StopPlayback();
            await ShowFrameAsync(_currentFrameIndex - 1);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Right && _archive is not null)
        {
            StopPlayback();
            await ShowFrameAsync(_currentFrameIndex + 1);
            eventArgs.Handled = true;
        }
    }

    private void Window_DragOver(object sender, DragEventArgs eventArgs)
    {
        eventArgs.Effects = TryDroppedZip(eventArgs.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        eventArgs.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs eventArgs)
    {
        if (TryDroppedZip(eventArgs.Data, out string path)) await OpenArchiveAsync(path);
    }

    private static bool TryDroppedZip(IDataObject data, out string path)
    {
        path = string.Empty;
        if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files) return false;
        path = files[0];
        return path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    private void SetReady(bool ready, string status)
    {
        TimelineSlider.IsEnabled = ready;
        PreviousButton.IsEnabled = ready;
        NextButton.IsEnabled = ready;
        PlayPauseButton.IsEnabled = ready;
        StatusText.Text = status;
    }

    private void ShowPreviewMessage(string message, bool error = false)
    {
        PreviewMessageText.Text = message;
        PreviewMessageIcon.Data = (Geometry)FindResource(error ? "Lucide.TriangleAlert" : "Lucide.FileArchive");
        PreviewMessageIcon.SetResourceReference(ForegroundProperty, error ? "DangerBrush" : "InkSoftBrush");
        PreviewMessageBorder.Visibility = Visibility.Visible;
    }

    private void StartPrefetch(int center, int radius)
    {
        if (_archive is null || _frameCache is null) return;
        _prefetchCancellation = new CancellationTokenSource();
        _ = PrefetchAsync(_archive, _frameCache, center, radius, _prefetchCancellation.Token);
    }

    private static async Task PrefetchAsync(DeepDebugArchive archive, DeepDebugFrameCache cache,
        int center, int radius, CancellationToken cancellationToken)
    {
        try
        {
            for (int offset = 1; offset <= radius; offset++)
            {
                foreach (int index in new[] { center + offset, center - offset })
                {
                    if (index < 0 || index >= archive.Frames.Count || !archive.Frames[index].EntryExists) continue;
                    try
                    {
                        await cache.GetAsync(index, cancellationToken);
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException)
                    {
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void StopPrefetch()
    {
        _prefetchCancellation?.Cancel();
        _prefetchCancellation?.Dispose();
        _prefetchCancellation = null;
    }

    private void DisposeArchive()
    {
        StopPlayback();
        StopPrefetch();
        _openCancellation?.Cancel();
        _displayCancellation?.Cancel();
        _frameCache?.Clear();
        _archive?.Dispose();
    }

    private void Theme_Click(object sender, RoutedEventArgs eventArgs)
    {
        AppThemeManager.Toggle();
        ThemeIcon.Data = (Geometry)FindResource(AppThemeManager.Current == AppTheme.Light ? "Lucide.Moon" : "Lucide.Sun");
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton == MouseButton.Left && eventArgs.ClickCount == 2) ToggleMaximize();
        else if (eventArgs.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs eventArgs) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs eventArgs) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs eventArgs) => Close();
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private static string DefaultDiagnosticsDirectory()
    {
        string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LilacMacro", "diagnostics");
        return Directory.Exists(path) ? path : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private static string Friendly(string value) => value.Replace('_', ' ').Replace('-', ' ').ToUpperInvariant();
    private static string FormatRuntime(TimeSpan? value) => value is { } runtime ? runtime.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture) : "—";
    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / (1024d * 1024 * 1024):0.0} GB"
        : $"{bytes / (1024d * 1024):0} MB";

    private sealed record NearbyEventItem(string Time, string Heading, string Details);
}

internal static class DeepDebugFrameListExtensions
{
    public static int FindIndex(this IReadOnlyList<DeepDebugFrameRecord> frames, Func<DeepDebugFrameRecord, bool> predicate)
    {
        for (int index = 0; index < frames.Count; index++) if (predicate(frames[index])) return index;
        return -1;
    }
}
