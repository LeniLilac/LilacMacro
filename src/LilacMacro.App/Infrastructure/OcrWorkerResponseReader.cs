using System.Text;

namespace LilacMacro.App.Infrastructure;

internal sealed class OcrWorkerResponseAccessException(string message, Exception innerException)
    : IOException(message, innerException);

internal static class OcrWorkerResponseReader
{
    internal const int MaximumAttempts = 20;
    internal const int RetryMilliseconds = 25;
    private const int MaximumBytes = 1024 * 1024;

    public static async Task<string> ReadAsync(string path, CancellationToken cancellationToken)
    {
        Exception? lastAccessError = null;
        for (int attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                await using FileStream stream = new(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (stream.Length > MaximumBytes)
                    throw new InvalidDataException("OCR result exceeded the safe size limit.");
                using StreamReader reader = new(
                    stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
                return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                lastAccessError = error;
                if (attempt < MaximumAttempts)
                {
                    await Task.Delay(RetryMilliseconds, cancellationToken).ConfigureAwait(false);
                    continue;
                }
            }
            break;
        }
        throw new OcrWorkerResponseAccessException(
            "OCR worker response remained temporarily unavailable.",
            lastAccessError ?? new IOException("OCR worker response could not be opened."));
    }
}
