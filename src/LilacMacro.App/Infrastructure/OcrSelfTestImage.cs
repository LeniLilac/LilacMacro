using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LilacMacro.App.Infrastructure;

internal static class OcrSelfTestImage
{
    internal const int Width = 640;
    internal const int Height = 180;

    internal static void Write(string path)
    {
        DrawingVisual visual = new();
        using (DrawingContext drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, Width, Height));
            FormattedText text = new(
                "TEST",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                104,
                Brushes.Black,
                1);
            drawing.DrawText(text, new Point((Width - text.Width) / 2, (Height - text.Height) / 2));
        }

        RenderTargetBitmap bitmap = new(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }
}
