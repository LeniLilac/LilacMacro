using System.Windows.Media;
using System.Windows.Media.Imaging;
using LilacMacro.Core.Datasets;

namespace LilacMacro.App.Views;

internal sealed record FrameListItem(DatasetFrame Frame, int Index, ImageSource Thumbnail)
{
    public string Number => $"#{Index + 1:0000}";

    public string Verdict => Frame.Verdict switch
    {
        FrameVerdict.Positive => "POS",
        FrameVerdict.Negative => "NEG",
        FrameVerdict.Ignore => "SKIP",
        _ => "NEW",
    };

    public string RegionCount => $"{Frame.Annotations.Count} BOX";
}

internal sealed record OcrResultItem(
    string Model,
    string Text,
    string Confidence,
    string Timings,
    string LineData,
    string Runtime);

internal static class ReviewImages
{
    public static BitmapImage LoadThumbnail(string path)
    {
        BitmapImage bitmap = new();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 180;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
