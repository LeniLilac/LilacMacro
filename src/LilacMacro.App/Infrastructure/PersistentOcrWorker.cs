using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LilacMacro.Core.Geometry;

namespace LilacMacro.App.Infrastructure;

internal sealed class PersistentOcrWorker(Func<ProcessStartInfo> createStartInfo) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _errorLock = new();
    private readonly StringBuilder _errorTail = new();
    private Process? _process;
    private string? _channel;
    private bool _disposed;

    public async Task WarmUpAsync(
        string modelName,
        string device,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Stop();
            Process process = EnsureWorker(modelName, device);
            await WaitForReadyAsync(process, cancellationToken);
        }
        catch
        {
            Stop();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OcrWorkerResult> RunAsync(
        string imagePath,
        PixelRect crop,
        string cropPath,
        string modelName,
        string device,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Process process = EnsureWorker();
            string requestId = Guid.NewGuid().ToString("N");
            string requestPath = Path.Combine(_channel!, $"request-{requestId}.json");
            string responsePath = Path.Combine(_channel!, $"response-{requestId}.json");
            string temporaryRequest = requestPath + ".tmp";
            string request = JsonSerializer.Serialize(new
            {
                request_id = requestId,
                input = imagePath,
                crop = new[] { crop.X, crop.Y, crop.Width, crop.Height },
                crop_output = cropPath,
                model = modelName,
                device,
            });
            await File.WriteAllTextAsync(
                temporaryRequest,
                request,
                new UTF8Encoding(false),
                cancellationToken);
            File.Move(temporaryRequest, requestPath);

            using CancellationTokenSource timeout = CreateTimeout(cancellationToken);
            try
            {
                await WaitForFileAsync(process, responsePath, timeout.Token);
                FileInfo resultFile = new(responsePath);
                if (resultFile.Length > 1024 * 1024)
                    throw new InvalidDataException("OCR result exceeded the safe size limit.");
                string json = await File.ReadAllTextAsync(responsePath, timeout.Token);
                using JsonDocument document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("error", out JsonElement error))
                    throw new InvalidOperationException($"OCR worker failed: {error.GetString()}");
                return JsonSerializer.Deserialize<OcrWorkerResult>(json)
                    ?? throw new InvalidDataException("OCR worker returned an empty result.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("OCR worker did not finish within 2 minutes.");
            }
            finally
            {
                TryDeleteFile(requestPath);
                TryDeleteFile(responsePath);
                TryDeleteFile(temporaryRequest);
            }
        }
        catch
        {
            Stop();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Stop()
    {
        Process? process = _process;
        string? channel = _channel;
        _process = null;
        _channel = null;
        if (process is null)
        {
            if (channel is not null) TryDeleteTemporaryDirectory(channel);
            return;
        }
        try
        {
            if (!process.HasExited)
            {
                if (channel is not null) File.WriteAllText(Path.Combine(channel, "stop"), string.Empty);
                if (!process.WaitForExit(750)) process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The owned worker exited between the state check and shutdown.
        }
        finally
        {
            process.Dispose();
            if (channel is not null) TryDeleteTemporaryDirectory(channel);
        }
    }

    private Process EnsureWorker(string? preloadModel = null, string? preloadDevice = null)
    {
        if (_process is { HasExited: false } running) return running;
        Stop();
        lock (_errorLock) _errorTail.Clear();
        string channel = Path.Combine(Path.GetTempPath(), "LilacMacro", $"worker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(channel);
        ProcessStartInfo startInfo = createStartInfo();
        startInfo.ArgumentList.Add("--serve");
        startInfo.ArgumentList.Add("--channel");
        startInfo.ArgumentList.Add(channel);
        if (preloadModel is not null && preloadDevice is not null)
        {
            startInfo.ArgumentList.Add("--preload-model");
            startInfo.ArgumentList.Add(preloadModel);
            startInfo.ArgumentList.Add("--preload-device");
            startInfo.ArgumentList.Add(preloadDevice);
        }
        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the persistent OCR worker.");
        process.ErrorDataReceived += Worker_OnErrorDataReceived;
        process.OutputDataReceived += Worker_OnOutputDataReceived;
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        _process = process;
        _channel = channel;
        return process;
    }

    private async Task WaitForReadyAsync(Process process, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CreateTimeout(cancellationToken);
        try
        {
            await WaitForFileAsync(process, Path.Combine(_channel!, "ready"), timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("OCR worker did not preload within 2 minutes.");
        }
    }

    private async Task WaitForFileAsync(Process process, string path, CancellationToken cancellationToken)
    {
        while (!File.Exists(path))
        {
            if (process.HasExited)
                throw new InvalidOperationException($"OCR worker stopped unexpectedly. {ReadErrorTail()}");
            await Task.Delay(25, cancellationToken);
        }
    }

    private static CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
    {
        CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        return timeout;
    }

    private void Worker_OnErrorDataReceived(object sender, DataReceivedEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(eventArgs.Data)) return;
        lock (_errorLock)
        {
            _errorTail.AppendLine(eventArgs.Data);
            if (_errorTail.Length > 4000) _errorTail.Remove(0, _errorTail.Length - 4000);
        }
    }

    private static void Worker_OnOutputDataReceived(object sender, DataReceivedEventArgs eventArgs)
    {
        // Paddle writes native diagnostics to stdout; draining prevents a full pipe from stalling inference.
    }

    private string ReadErrorTail()
    {
        lock (_errorLock) return _errorTail.ToString().Trim();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // The worker session cleanup retries when the owned process is released.
        }
    }

    private static void TryDeleteTemporaryDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // The OS temp cleaner can remove a channel left by a recently released worker.
        }
        catch (UnauthorizedAccessException)
        {
            // The OS temp cleaner can remove a channel left by a recently released worker.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _gate.Dispose();
    }
}
