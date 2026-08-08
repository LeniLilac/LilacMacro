using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;

namespace LilacMacro.App.Controls;

public partial class OcrVisualization : UserControl
{
    private static readonly Brush SmallBrush = new SolidColorBrush(Color.FromRgb(61, 140, 255));
    private static readonly Brush TinyBrush = new SolidColorBrush(Color.FromRgb(255, 79, 172));
    private static readonly Brush EmptyBrush = new SolidColorBrush(Color.FromRgb(117, 126, 143));
    private static readonly Brush RoiBrush = new SolidColorBrush(Color.FromRgb(255, 225, 90));
    private Point? _panStart;
    private double _panHorizontalOffset;
    private double _panVerticalOffset;
    private double _zoom = 1;

    public OcrVisualization()
    {
        InitializeComponent();
    }

    public event EventHandler? ZoomChanged;

    public double Zoom => _zoom;

    public bool TextOnly { get; private set; }

    public bool HideUnchecked { get; private set; }

    public void SetHideUnchecked(bool value) => HideUnchecked = value;

    public void SetTextOnly(bool value)
    {
        if (TextOnly == value) return;
        TextOnly = value;
        SourcePanel.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        SourceColumn.Width = value ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        GapColumn.Width = value ? new GridLength(0) : new GridLength(14);
        TextColumn.Width = new GridLength(1, GridUnitType.Star);
        Dispatcher.BeginInvoke(FitToViewport);
    }

    public void ShowFrame(
        string imagePath,
        DatasetFrame frame,
        Guid? selectedId,
        string preferredModel,
        string preferredDevice)
    {
        BitmapImage bitmap = LoadBitmap(imagePath);
        SourceSurface.Width = bitmap.PixelWidth;
        SourceSurface.Height = bitmap.PixelHeight;
        TextSurface.Width = bitmap.PixelWidth;
        TextSurface.Height = bitmap.PixelHeight;
        SourceImage.Source = bitmap;
        SourceOverlay.Children.Clear();
        TextOverlay.Children.Clear();

        foreach (BoxAnnotation annotation in frame.Annotations)
        {
            OcrTrial? trial = Latest(annotation, preferredModel, preferredDevice);
            Brush color = TrialBrush(trial);
            AddParentRegion(annotation, selectedId, color);
            if (trial is { Regions.Count: > 0 })
            {
                foreach (OcrTextRegion region in trial.Regions)
                {
                    if (HideUnchecked && !HasRole(region)) continue;
                    AddDetectedSourceRegion(region, color);
                    AddDetectedTextRegion(region, color);
                }
                AddMetrics(annotation.Bounds, trial, color);
            }
            else
            {
                AddLegacyTextRegion(annotation, selectedId, color, trial);
            }
        }
    }

    public void Clear()
    {
        SourceImage.Source = null;
        SourceOverlay.Children.Clear();
        TextOverlay.Children.Clear();
    }

    public void ZoomIn() => SetZoom(_zoom * 1.25);

    public void ZoomOut() => SetZoom(_zoom / 1.25);

    public void FitToViewport() => SetZoom(1);

    private void AddParentRegion(BoxAnnotation annotation, Guid? selectedId, Brush color)
    {
        bool selected = annotation.Id == selectedId;
        Rectangle box = new()
        {
            Width = annotation.Bounds.Width,
            Height = annotation.Bounds.Height,
            Stroke = selected ? RoiBrush : color,
            StrokeThickness = selected ? 5 : 2,
            StrokeDashArray = [7, 4],
            Fill = selected ? new SolidColorBrush(Color.FromArgb(24, 255, 225, 90)) : Brushes.Transparent,
        };
        Position(box, annotation.Bounds.X, annotation.Bounds.Y, SourceOverlay);
    }

    private void AddDetectedSourceRegion(OcrTextRegion region, Brush color)
    {
        PixelRect bounds = region.Bounds;
        Rectangle box = new()
        {
            Width = bounds.Width,
            Height = bounds.Height,
            Stroke = color,
            StrokeThickness = HasRole(region) ? 5 : 3,
            StrokeDashArray = HasRole(region) ? [6, 3] : null,
            Fill = Brushes.Transparent,
        };
        Position(box, bounds.X, bounds.Y, SourceOverlay);
        TextBlock label = MakeLabel(
            $"{region.Text}{RoleSuffix(region)}  [{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}]",
            color,
            10);
        Position(label, bounds.X, Math.Max(0, bounds.Y - 19), SourceOverlay);
    }

    private void AddDetectedTextRegion(OcrTextRegion region, Brush color)
    {
        PixelRect bounds = region.Bounds;
        Border box = new()
        {
            Width = bounds.Width,
            Height = bounds.Height,
            BorderBrush = color,
            BorderThickness = new Thickness(HasRole(region) ? 4 : 2),
            Background = Brushes.White,
            Padding = new Thickness(3, 0, 3, 0),
            Child = new TextBlock
            {
                Text = region.Text + RoleSuffix(region),
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = Math.Clamp(bounds.Height * 0.58, 9, 28),
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Black,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Position(box, bounds.X, bounds.Y, TextOverlay);
    }

    private void AddMetrics(PixelRect parent, OcrTrial trial, Brush color)
    {
        long total = trial.ModelLoadMilliseconds + trial.InferenceMilliseconds;
        string load = trial.ModelWasCached ? "cached" : $"load {trial.ModelLoadMilliseconds} ms";
        TextBlock metrics = MakeLabel(
            $"{trial.Regions.Count} BOX  {trial.Device.ToUpperInvariant()}  {trial.Confidence:P1}  {load}  inference {trial.InferenceMilliseconds} ms  total {total} ms",
            color,
            10);
        Position(metrics, parent.X, Math.Min(TextSurface.Height - 19, parent.Bottom + 2), TextOverlay);
    }

    private void AddLegacyTextRegion(BoxAnnotation annotation, Guid? selectedId, Brush color, OcrTrial? trial)
    {
        string display = trial is null
            ? (string.IsNullOrWhiteSpace(annotation.Label) ? "Not tested" : annotation.Label)
            : (string.IsNullOrWhiteSpace(trial.Text) ? "No text" : trial.Text);
        Border region = new()
        {
            Width = annotation.Bounds.Width,
            Height = annotation.Bounds.Height,
            BorderBrush = color,
            BorderThickness = new Thickness(annotation.Id == selectedId ? 5 : 3),
            Background = Brushes.White,
            Padding = new Thickness(5, 2, 5, 2),
            Child = new TextBlock
            {
                Text = display,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = Math.Clamp(annotation.Bounds.Height * 0.3, 10, 30),
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Black,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Position(region, annotation.Bounds.X, annotation.Bounds.Y, TextOverlay);
    }

    private static OcrTrial? Latest(BoxAnnotation annotation, string preferredModel, string preferredDevice) => annotation.OcrTrials
        .Where(trial => string.Equals(trial.ModelName, preferredModel, StringComparison.Ordinal) &&
                        string.Equals(trial.Device, preferredDevice, StringComparison.Ordinal))
        .OrderByDescending(trial => trial.RanAtUtc)
        .FirstOrDefault()
        ?? annotation.OcrTrials.OrderByDescending(trial => trial.RanAtUtc).FirstOrDefault();

    private static Brush TrialBrush(OcrTrial? trial) => trial?.ModelName switch
    {
        "PP-OCRv6_small_rec" => SmallBrush,
        "PP-OCRv6_tiny_rec" => TinyBrush,
        _ => EmptyBrush,
    };

    private static bool HasRole(OcrTextRegion region) => region.IsOcrEvidence || region.IsVisualAnchor;

    private static string RoleSuffix(OcrTextRegion region) =>
        (region.IsOcrEvidence, region.IsVisualAnchor, region.MatchMode == OcrMatchMode.FuzzyPhrase) switch
        {
            (true, true, true) => "  [OCR:FUZZY+IMAGE]",
            (true, true, false) => "  [OCR+IMAGE]",
            (true, false, true) => "  [OCR:FUZZY]",
            (true, false, false) => "  [OCR]",
            (false, true, _) => "  [IMAGE]",
            _ => string.Empty,
        };

    private void SetZoom(double value, Point? anchor = null)
    {
        double next = Math.Clamp(value, 0.25, 8);
        Point viewportAnchor = anchor ?? new Point(Viewport.ViewportWidth / 2, Viewport.ViewportHeight / 2);
        double logicalX = (Viewport.HorizontalOffset + viewportAnchor.X) / _zoom;
        double logicalY = (Viewport.VerticalOffset + viewportAnchor.Y) / _zoom;
        _zoom = next;
        ZoomTransform.ScaleX = next;
        ZoomTransform.ScaleY = next;
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        Dispatcher.BeginInvoke(() =>
        {
            Viewport.ScrollToHorizontalOffset(logicalX * next - viewportAnchor.X);
            Viewport.ScrollToVerticalOffset(logicalY * next - viewportAnchor.Y);
        });
    }

    private void Viewport_OnPreviewMouseWheel(object sender, MouseWheelEventArgs eventArgs)
    {
        Point anchor = eventArgs.GetPosition(Viewport);
        SetZoom(eventArgs.Delta > 0 ? _zoom * 1.15 : _zoom / 1.15, anchor);
        eventArgs.Handled = true;
    }

    private void Viewport_OnPreviewMouseDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Middle) return;
        _panStart = eventArgs.GetPosition(Viewport);
        _panHorizontalOffset = Viewport.HorizontalOffset;
        _panVerticalOffset = Viewport.VerticalOffset;
        Viewport.CaptureMouse();
        Viewport.Cursor = Cursors.ScrollAll;
        eventArgs.Handled = true;
    }

    private void Viewport_OnMouseMove(object sender, MouseEventArgs eventArgs)
    {
        if (_panStart is not { } start || eventArgs.MiddleButton != MouseButtonState.Pressed) return;
        Point current = eventArgs.GetPosition(Viewport);
        Viewport.ScrollToHorizontalOffset(_panHorizontalOffset - (current.X - start.X));
        Viewport.ScrollToVerticalOffset(_panVerticalOffset - (current.Y - start.Y));
        eventArgs.Handled = true;
    }

    private void Viewport_OnPreviewMouseUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.ChangedButton != MouseButton.Middle) return;
        EndPan();
        eventArgs.Handled = true;
    }

    private void Viewport_OnLostMouseCapture(object sender, MouseEventArgs eventArgs) => EndPan(releaseCapture: false);

    private void EndPan(bool releaseCapture = true)
    {
        _panStart = null;
        Viewport.Cursor = null;
        if (releaseCapture && Mouse.Captured == Viewport) Viewport.ReleaseMouseCapture();
    }

    private void Viewport_OnSizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        ZoomSurface.Width = Math.Max(1, eventArgs.NewSize.Width);
        ZoomSurface.Height = Math.Max(1, eventArgs.NewSize.Height);
    }

    private static TextBlock MakeLabel(string text, Brush background, double size) => new()
    {
        Text = text,
        Background = background,
        Foreground = Brushes.White,
        FontSize = size,
        FontWeight = FontWeights.Bold,
        Padding = new Thickness(4, 2, 4, 2),
    };

    private static void Position(UIElement element, double x, double y, Panel panel)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        panel.Children.Add(element);
    }

    private static BitmapImage LoadBitmap(string path)
    {
        BitmapImage bitmap = new();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
