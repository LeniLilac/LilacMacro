using System.Diagnostics;
using System.Windows.Media.Imaging;

namespace LilacMacro.App.Diagnostics;

internal interface IDeepDebugFrameCodec
{
    Task<DeepDebugFrameEncodingResult> EncodeAsync(
        string pngPath,
        bool lossless,
        bool waitForLease,
        CancellationToken cancellationToken = default);
}

internal sealed record DeepDebugFrameEncodingResult(
    bool Success,
    byte[]? Bytes,
    string Validation,
    string Format,
    int? Quality = null);

internal sealed class DeepDebugFrameCodec(string diagnosticsRoot) : IDeepDebugFrameCodec
{
    private const int LossyJpegQuality = 14;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly string _encoder = Path.Combine(AppContext.BaseDirectory, "tools", "avif", "avifenc.exe");
    private readonly string _decoder = Path.Combine(AppContext.BaseDirectory, "tools", "avif", "avifdec.exe");
    private readonly string _lockPath = Path.Combine(diagnosticsRoot, ".avif-encoder.lock");

    public async Task<DeepDebugFrameEncodingResult> EncodeAsync(
        string pngPath,
        bool lossless,
        bool waitForLease,
        CancellationToken cancellationToken = default)
    {
        if (!lossless) return await EncodeJpegAsync(pngPath, cancellationToken);
        if (!File.Exists(_encoder) || !File.Exists(_decoder))
            return new(false, null, "codec-unavailable", "avif");
        Directory.CreateDirectory(Path.GetDirectoryName(_lockPath)!);
        await using FileStream? lease = await AcquireAsync(waitForLease, cancellationToken);
        if (lease is null) return new(false, null, "encoder-busy", "avif");
        string temporaryRoot = Path.Combine(Path.GetTempPath(), "LilacMacro", "avif", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        string encoded = Path.Combine(temporaryRoot, "frame.avif");
        string decoded = Path.Combine(temporaryRoot, "decoded.png");
        try
        {
            List<string> encodeArguments =
                ["-l", "-s", "8", "-j", "all", "--ignore-exif", "--ignore-xmp", "--ignore-icc", pngPath, encoded];
            if (!await RunAsync(_encoder, encodeArguments, cancellationToken) || !File.Exists(encoded))
                return new(false, null, "encode-failed", "avif");
            if (!await RunAsync(_decoder,
                    ["-j", "all", "--size-limit", "67108864", "--dimension-limit", "16384", encoded, decoded],
                    cancellationToken) || !File.Exists(decoded))
                return new(false, null, "decode-failed", "avif");

            byte[] original = await File.ReadAllBytesAsync(pngPath, cancellationToken);
            byte[] roundTrip = await File.ReadAllBytesAsync(decoded, cancellationToken);
            (int width, int height, byte[] digest) = DeepDebugPerceptualHash.CreatePixelDigest(original);
            (int decodedWidth, int decodedHeight, byte[] decodedDigest) =
                DeepDebugPerceptualHash.CreatePixelDigest(roundTrip);
            if (width != decodedWidth || height != decodedHeight)
                return new(false, null, "dimension-mismatch", "avif");
            if (!digest.SequenceEqual(decodedDigest))
                return new(false, null, "pixel-mismatch", "avif");
            byte[] bytes = await File.ReadAllBytesAsync(encoded, cancellationToken);
            if (bytes.LongLength >= original.LongLength)
                return new(false, null, "not-smaller", "avif");
            return new(true, bytes, "pixel-exact", "avif");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
                                      OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            return new(false, null,
                error is OperationCanceledException ? "timeout-or-cancelled" : "validation-failed",
                "avif");
        }
        finally
        {
            try { Directory.Delete(temporaryRoot, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static async Task<DeepDebugFrameEncodingResult> EncodeJpegAsync(
        string pngPath,
        CancellationToken cancellationToken)
    {
        try
        {
            byte[] original = await File.ReadAllBytesAsync(pngPath, cancellationToken);
            return await Task.Run<DeepDebugFrameEncodingResult>(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] encoded;
                int width;
                int height;
                using (MemoryStream source = new(original, writable: false))
                {
                    BitmapDecoder decoder = BitmapDecoder.Create(
                        source,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    width = decoder.Frames[0].PixelWidth;
                    height = decoder.Frames[0].PixelHeight;
                    JpegBitmapEncoder encoder = new() { QualityLevel = LossyJpegQuality };
                    encoder.Frames.Add(BitmapFrame.Create(decoder.Frames[0]));
                    using MemoryStream output = new();
                    encoder.Save(output);
                    encoded = output.ToArray();
                }

                using (MemoryStream verification = new(encoded, writable: false))
                {
                    BitmapDecoder decoded = BitmapDecoder.Create(
                        verification,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    if (width != decoded.Frames[0].PixelWidth || height != decoded.Frames[0].PixelHeight)
                        return new(false, null, "dimension-mismatch", "jpeg", LossyJpegQuality);
                }
                if (encoded.LongLength >= original.LongLength)
                    return new(false, null, "not-smaller", "jpeg", LossyJpegQuality);
                return new(true, encoded, "decode-verified", "jpeg", LossyJpegQuality);
            }, cancellationToken);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
                                      OperationCanceledException or NotSupportedException or ArgumentException)
        {
            return new(false, null,
                error is OperationCanceledException ? "timeout-or-cancelled" : "validation-failed",
                "jpeg",
                LossyJpegQuality);
        }
    }

    private async Task<FileStream?> AcquireAsync(bool waitForLease, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = waitForLease ? DateTimeOffset.UtcNow + Timeout : DateTimeOffset.UtcNow;
        bool firstAttempt = true;
        while (firstAttempt || DateTimeOffset.UtcNow < deadline)
        {
            firstAttempt = false;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                FileStream lease = new(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                try { File.SetAttributes(_lockPath, File.GetAttributes(_lockPath) | FileAttributes.Hidden); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                return lease;
            }
            catch (IOException) { await Task.Delay(100, cancellationToken); }
        }
        return null;
    }

    private static async Task<bool> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        using Process process = new() { StartInfo = new(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true } };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            await Task.WhenAll(output, errors);
            return false;
        }
        await Task.WhenAll(output, errors);
        return process.ExitCode == 0;
    }
}

internal static class DeepDebugAvifDisplayDecoder
{
    public static async Task<byte[]> DecodeAsync(byte[] avif, CancellationToken cancellationToken)
    {
        string decoder = Path.Combine(AppContext.BaseDirectory, "tools", "avif", "avifdec.exe");
        if (!File.Exists(decoder))
            throw new InvalidOperationException("The bundled AVIF decoder is unavailable. Repair or reinstall LilacMacro.");
        string root = Path.Combine(Path.GetTempPath(), "LilacMacro", "avif-viewer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "frame.avif");
        string output = Path.Combine(root, "frame.png");
        try
        {
            await File.WriteAllBytesAsync(source, avif, cancellationToken);
            using Process process = new() { StartInfo = new(decoder) { UseShellExecute = false, CreateNoWindow = true } };
            foreach (string argument in new[] { "-j", "all", "--size-limit", "67108864", "--dimension-limit", "16384", source, output })
                process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw new InvalidDataException("The AVIF frame exceeded its decode deadline.");
            }
            if (process.ExitCode != 0 || !File.Exists(output))
                throw new InvalidDataException("The AVIF frame failed bounded decoding.");
            byte[] png = await File.ReadAllBytesAsync(output, cancellationToken);
            _ = DeepDebugPerceptualHash.CreatePixelDigest(png);
            return png;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
