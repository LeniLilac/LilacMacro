using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using LilacMacro.Core.Datasets;

namespace LilacMacro.DatasetTool;

internal sealed class DatasetValidator
{
    public async Task<IReadOnlyList<string>> ValidateAsync(
        DatasetLocation dataset,
        CancellationToken cancellationToken = default)
    {
        List<string> failures = [];
        HashSet<string> fileNames = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < dataset.Manifest.Frames.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DatasetFrame frame = dataset.Manifest.Frames[index];
            if (!fileNames.Add(frame.FileName))
            {
                failures.Add($"Frame {index + 1} repeats file name {frame.FileName}.");
                continue;
            }

            string imagePath = Path.Combine(dataset.ImagesPath, frame.FileName);
            if (!File.Exists(imagePath))
            {
                failures.Add($"Frame {index + 1} is missing {frame.FileName}.");
                continue;
            }

            await using FileStream stream = File.OpenRead(imagePath);
            string digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (!string.Equals(digest, frame.Sha256, StringComparison.Ordinal))
            {
                failures.Add($"Frame {index + 1} SHA-256 does not match dataset.json.");
            }

            try
            {
                BitmapFrame bitmap = LoadBitmap(imagePath);
                if (bitmap.PixelWidth != frame.Width || bitmap.PixelHeight != frame.Height)
                {
                    failures.Add(
                        $"Frame {index + 1} PNG is {bitmap.PixelWidth} × {bitmap.PixelHeight}; " +
                        $"manifest says {frame.Width} × {frame.Height}.");
                }
            }
            catch (Exception error) when (error is IOException or NotSupportedException)
            {
                failures.Add($"Frame {index + 1} is not a readable PNG: {error.Message}");
            }
        }

        return failures;
    }

    internal static BitmapFrame LoadBitmap(string path)
    {
        using FileStream stream = File.OpenRead(path);
        BitmapDecoder decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        BitmapFrame frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }
}
