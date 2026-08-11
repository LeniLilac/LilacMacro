using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LilacMacro.App.Views;

internal static class PlacementTimelineSnapshot
{
    public static ImageBrush Capture(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        DpiScale dpi = VisualTreeHelper.GetDpi(element);
        Size logicalSize = new(
            Math.Max(1, element.ActualWidth),
            Math.Max(1, element.ActualHeight));
        int pixelWidth = Math.Max(1, (int)Math.Ceiling(logicalSize.Width * dpi.DpiScaleX));
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(logicalSize.Height * dpi.DpiScaleY));

        RenderTargetBitmap bitmap = new(
            pixelWidth,
            pixelHeight,
            96 * dpi.DpiScaleX,
            96 * dpi.DpiScaleY,
            PixelFormats.Pbgra32);

        DrawingVisual localVisual = new();
        using (DrawingContext drawingContext = localVisual.RenderOpen())
        {
            VisualBrush source = new(element) { Stretch = Stretch.Fill };
            drawingContext.DrawRectangle(source, null, new Rect(new Point(), logicalSize));
        }

        bitmap.Render(localVisual);
        bitmap.Freeze();
        ImageBrush brush = new(bitmap) { Stretch = Stretch.Fill };
        brush.Freeze();
        return brush;
    }
}
