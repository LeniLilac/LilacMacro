using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;

namespace LilacMacro.App.Controls;

public partial class AnnotationSurface : UserControl
{
    private static readonly Brush RegionBrush = new SolidColorBrush(Color.FromArgb(40, 255, 79, 172));
    private static readonly Brush RegionStroke = new SolidColorBrush(Color.FromRgb(255, 79, 172));
    private static readonly Brush SelectedBrush = new SolidColorBrush(Color.FromArgb(62, 61, 140, 255));
    private static readonly Brush SelectedStroke = new SolidColorBrush(Color.FromRgb(61, 140, 255));
    private IReadOnlyList<BoxAnnotation> _annotations = [];
    private PixelSize _imageSize = new(1280, 720);
    private Point? _dragStart;
    private Rectangle? _draftRectangle;
    private Guid? _selectedId;
    private double _zoom = 1;
    private bool _fitMode = true;
    private Point? _panStart;
    private double _panHorizontalOffset;
    private double _panVerticalOffset;

    public AnnotationSurface()
    {
        InitializeComponent();
    }

    public event EventHandler<PixelRect>? RegionCreated;

    public event EventHandler<Guid>? RegionSelected;

    public event EventHandler? ZoomChanged;

    public double Zoom => _zoom;

    public void ShowFrame(string imagePath, DatasetFrame frame, Guid? selectedId)
    {
        ArgumentNullException.ThrowIfNull(frame);
        BitmapImage bitmap = LoadBitmap(imagePath);
        _imageSize = new PixelSize(bitmap.PixelWidth, bitmap.PixelHeight);
        _annotations = frame.Annotations;
        _selectedId = selectedId;
        ImageSurface.Width = _imageSize.Width;
        ImageSurface.Height = _imageSize.Height;
        FrameImage.Source = bitmap;
        RenderAnnotations();
        if (_fitMode) Dispatcher.BeginInvoke(FitToViewport);
    }

    public void Clear()
    {
        FrameImage.Source = null;
        _annotations = [];
        _selectedId = null;
        Overlay.Children.Clear();
    }

    public void Select(Guid? id)
    {
        _selectedId = id;
        RenderAnnotations();
    }

    public void ZoomIn() => SetZoom(_zoom * 1.25, fitMode: false);

    public void ZoomOut() => SetZoom(_zoom / 1.25, fitMode: false);

    public void ActualSize() => SetZoom(1, fitMode: false);

    public void FitToViewport()
    {
        if (_imageSize.Width <= 0 || _imageSize.Height <= 0 || Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0)
        {
            return;
        }

        double availableWidth = Math.Max(1, Viewport.ActualWidth - 4);
        double availableHeight = Math.Max(1, Viewport.ActualHeight - 4);
        double fit = Math.Min(availableWidth / _imageSize.Width, availableHeight / _imageSize.Height);
        SetZoom(fit, fitMode: true);
    }

    private void SetZoom(double value, bool fitMode, Point? anchor = null)
    {
        double next = Math.Clamp(value, 0.1, 8);
        Point viewportAnchor = anchor ?? new Point(Viewport.ViewportWidth / 2, Viewport.ViewportHeight / 2);
        double logicalX = (Viewport.HorizontalOffset + viewportAnchor.X) / _zoom;
        double logicalY = (Viewport.VerticalOffset + viewportAnchor.Y) / _zoom;
        _fitMode = fitMode;
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

    private void RenderAnnotations()
    {
        Overlay.Children.Clear();
        foreach (BoxAnnotation annotation in _annotations)
        {
            bool selected = annotation.Id == _selectedId;
            Rectangle rectangle = new()
            {
                Width = annotation.Bounds.Width,
                Height = annotation.Bounds.Height,
                Fill = selected ? SelectedBrush : RegionBrush,
                Stroke = selected ? SelectedStroke : RegionStroke,
                StrokeThickness = selected ? 4 : 3,
                Tag = annotation.Id,
                Cursor = Cursors.Hand,
                ToolTip = BuildToolTip(annotation),
            };
            rectangle.MouseLeftButtonDown += Region_OnMouseLeftButtonDown;
            Canvas.SetLeft(rectangle, annotation.Bounds.X);
            Canvas.SetTop(rectangle, annotation.Bounds.Y);
            Overlay.Children.Add(rectangle);
            AddRegionLabel(annotation, selected);
        }
    }

    private void AddRegionLabel(BoxAnnotation annotation, bool selected)
    {
        string label = string.IsNullOrWhiteSpace(annotation.Label) ? "UNLABELED" : annotation.Label;
        TextBlock text = new()
        {
            Text = $"{label}  [{annotation.Bounds.X},{annotation.Bounds.Y},{annotation.Bounds.Width},{annotation.Bounds.Height}]",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black,
            Background = selected ? SelectedStroke : new SolidColorBrush(Color.FromRgb(255, 225, 90)),
            Padding = new Thickness(5, 2, 5, 2),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(text, annotation.Bounds.X);
        Canvas.SetTop(text, Math.Max(0, annotation.Bounds.Y - 23));
        Overlay.Children.Add(text);
    }

    private void Region_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (sender is Rectangle { Tag: Guid id })
        {
            _selectedId = id;
            RenderAnnotations();
            RegionSelected?.Invoke(this, id);
            eventArgs.Handled = true;
        }
    }

    private void Overlay_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        _dragStart = Clamp(eventArgs.GetPosition(Overlay));
        _draftRectangle = new Rectangle
        {
            Stroke = SelectedStroke,
            StrokeThickness = 3,
            StrokeDashArray = [5, 3],
            Fill = SelectedBrush,
            IsHitTestVisible = false,
        };
        Overlay.Children.Add(_draftRectangle);
        Overlay.CaptureMouse();
    }

    private void Overlay_OnMouseMove(object sender, MouseEventArgs eventArgs)
    {
        if (_dragStart is not { } start || _draftRectangle is null) return;
        Point end = Clamp(eventArgs.GetPosition(Overlay));
        Canvas.SetLeft(_draftRectangle, Math.Min(start.X, end.X));
        Canvas.SetTop(_draftRectangle, Math.Min(start.Y, end.Y));
        _draftRectangle.Width = Math.Abs(end.X - start.X);
        _draftRectangle.Height = Math.Abs(end.Y - start.Y);
    }

    private void Overlay_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (_dragStart is not { } start) return;
        Point end = Clamp(eventArgs.GetPosition(Overlay));
        _dragStart = null;
        _draftRectangle = null;
        Overlay.ReleaseMouseCapture();
        PixelRect? region = PixelRect.FromDrag(start.X, start.Y, end.X, end.Y, _imageSize);
        RenderAnnotations();
        if (region is { } bounds) RegionCreated?.Invoke(this, bounds);
    }

    private void Viewport_OnPreviewMouseWheel(object sender, MouseWheelEventArgs eventArgs)
    {
        Point anchor = eventArgs.GetPosition(Viewport);
        SetZoom(eventArgs.Delta > 0 ? _zoom * 1.15 : _zoom / 1.15, fitMode: false, anchor);
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

    private void Viewport_OnPanMouseMove(object sender, MouseEventArgs eventArgs)
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
        if (_fitMode) FitToViewport();
    }

    private Point Clamp(Point point) => new(
        Math.Clamp(point.X, 0, _imageSize.Width),
        Math.Clamp(point.Y, 0, _imageSize.Height));

    private static string BuildToolTip(BoxAnnotation annotation) =>
        $"x={annotation.Bounds.X}, y={annotation.Bounds.Y}, width={annotation.Bounds.Width}, height={annotation.Bounds.Height}";

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
