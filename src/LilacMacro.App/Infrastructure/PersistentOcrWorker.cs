using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LilacMacro.Core.Geometry;

namespace LilacMacro.App.Infrastructure;

internal sealed class PersistentOcrWorker(
    Func<string?, ProcessStartInfo> createStartInfo,
    Action<OcrWorkerLifecycleEvent>? observe = null) : IDisposable
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _errorLock = new();
    private readonly StringBuilder _errorTail = new();
    private Process? _process;
    private string? _channel;
    private bool _disposed;

    internal static TimeSpan OperationDeadline => OperationTimeout;

    public async Task WarmUpAsync(
        string modelName,
        string device,
        CancellationToken cancellationToken,
        int scale = 1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stopwatch elapsed = Stopwatch.StartNew();
        Observe("worker_gate_waiting", "preload-gate", device, modelName, elapsed);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Observe("worker_gate_acquired", "preload-gate", device, modelName, elapsed);
            Stop();
            Process process = EnsureWorker(modelName, device);
            await WaitForReadyAsync(process, device, modelName, cancellationToken, elapsed);
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
        CancellationToken cancellationToken,
        int scale = 1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stopwatch elapsed = Stopwatch.StartNew();
        Observe("request_queued", "request-gate", device, modelName, elapsed);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Observe("request_gate_acquired", "request-gate", device, modelName, elapsed);
            Process process = EnsureWorker(preloadModel: null, preloadDevice: device);
            string requestId = Guid.NewGuid().ToString("N");
            string requestPath = Path.Combine(_channel!, $"request-{requestId}.json");
            string responsePath = Path.Combine(_channel!, $"response-{requestId}.json");
            string statusPath = Path.Combine(_channel!, $"status-{requestId}.json");
            string temporaryRequest = requestPath + ".tmp";
            string request = JsonSerializer.Serialize(new
            {
                request_id = requestId,
                input = imagePath,
                crop = new[] { crop.X, crop.Y, crop.Width, crop.Height },
                crop_output = cropPath,
                model = modelName,
                device,
                scale,
            });
            await File.WriteAllTextAsync(
                temporaryRequest,
                request,
                new UTF8Encoding(false),
                cancellationToken);
            File.Move(temporaryRequest, requestPath);
            Observe("request_sent", "response-wait", device, modelName, elapsed);

            using CancellationTokenSource timeout = CreateTimeout(cancellationToken);
            string phase = "response-wait";
            try
            {
                await WaitForFileAsync(
                    process,
                    responsePath,
                    timeout.Token,
                    statusPath,
                    observed =>
                    {
                        phase = observed;
                        Observe("worker_phase", observed, device, modelName, elapsed);
                    });
                Observe("response_detected", "response-read", device, modelName, elapsed);
                string json = await OcrWorkerResponseReader.ReadAsync(responsePath, timeout.Token)
                    .ConfigureAwait(false);
                using JsonDocument document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("error", out JsonElement error))
                    throw new InvalidOperationException($"OCR worker failed: {error.GetString()}");
                OcrWorkerResult result = JsonSerializer.Deserialize<OcrWorkerResult>(json)
                    ?? throw new InvalidDataException("OCR worker returned an empty result.");
                Observe("response_completed", "response-read", device, modelName, elapsed);
                return result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Observe("worker_timeout", phase, device, modelName, elapsed);
                throw new OcrWorkerTimeoutException(phase, OperationTimeout);
            }
            finally
            {
                TryDeleteFile(requestPath);
                TryDeleteFile(responsePath);
                TryDeleteFile(statusPath);
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
        if (_process is { HasExited: false } running)
        {
            Observe("worker_reused", "worker-start", preloadDevice, preloadModel, Stopwatch.StartNew());
            return running;
        }
        Stop();
        lock (_errorLock) _errorTail.Clear();
        string channel = Path.Combine(Path.GetTempPath(), "LilacMacro", $"worker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(channel);
        ProcessStartInfo startInfo = createStartInfo(preloadDevice);
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
        Stopwatch elapsed = Stopwatch.StartNew();
        Observe("worker_starting", "worker-start", preloadDevice, preloadModel, elapsed);
        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the persistent OCR worker.");
        process.ErrorDataReceived += Worker_OnErrorDataReceived;
        process.OutputDataReceived += Worker_OnOutputDataReceived;
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        _process = process;
        _channel = channel;
        Observe("worker_started", "worker-start", preloadDevice, preloadModel, elapsed);
        return process;
    }

    private async Task WaitForReadyAsync(
        Process process,
        string device,
        string modelName,
        CancellationToken cancellationToken,
        Stopwatch elapsed)
    {
        Observe("preload_waiting", "preload-ready", device, modelName, elapsed);
        using CancellationTokenSource timeout = CreateTimeout(cancellationToken);
        string phase = "preload-ready";
        try
        {
            await WaitForFileAsync(
                process,
                Path.Combine(_channel!, "ready"),
                timeout.Token,
                Path.Combine(_channel!, "preload-status.json"),
                observed =>
                {
                    phase = observed;
                    Observe("worker_phase", observed, device, modelName, elapsed);
                });
            Observe("preload_completed", "preload-ready", device, modelName, elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Observe("worker_timeout", phase, device, modelName, elapsed);
            throw new OcrWorkerTimeoutException(phase, OperationTimeout);
        }
    }

    private async Task WaitForFileAsync(
        Process process,
        string path,
        CancellationToken cancellationToken,
        string? statusPath = null,
        Action<string>? statusChanged = null)
    {
        string? lastStatus = null;
        while (!File.Exists(path))
        {
            if (process.HasExited)
                throw new InvalidOperationException($"OCR worker stopped unexpectedly. {ReadErrorTail()}");
            string? status = TryReadStatus(statusPath);
            if (!string.IsNullOrWhiteSpace(status) && status != lastStatus)
            {
                lastStatus = status;
                statusChanged?.Invoke(status);
            }
            await Task.Delay(25, cancellationToken);
        }
    }

    private static string? TryReadStatus(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        try
        {
            using JsonDocument status = JsonDocument.Parse(File.ReadAllText(path));
            return status.RootElement.TryGetProperty("stage", out JsonElement stage)
                ? stage.GetString()
                : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
    {
        CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OperationTimeout);
        return timeout;
    }

    private void Observe(
        string action,
        string stage,
        string? device,
        string? model,
        Stopwatch elapsed) => observe?.Invoke(new(
            action,
            stage,
            device,
            model,
            elapsed.ElapsedMilliseconds));

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
