using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using LilacMacro.Core.Geometry;

namespace LilacMacro.App.Infrastructure;

public sealed class OcrRunner : IDisposable
{
    public const string SmallModel = "PP-OCRv6_small_rec";
    public const string TinyModel = "PP-OCRv6_tiny_rec";
    public const string CpuDevice = "cpu";
    public const string GpuDevice = "gpu:0";
    private static readonly HashSet<string> SupportedModels = [SmallModel, TinyModel];
    private static readonly HashSet<string> SupportedDevices = [CpuDevice, GpuDevice];
    private readonly SemaphoreSlim _workerGate = new(1, 1);
    private readonly object _errorLock = new();
    private readonly StringBuilder _errorTail = new();
    private readonly string _pythonPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LilacMacro",
        "ocr",
        "venv",
        "Scripts",
        "python.exe");
    private readonly string _runtimeMarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LilacMacro",
        "ocr",
        "runtime-device.txt");
    private Process? _persistentWorker;
    private string? _persistentChannel;
    private bool _keepLoaded;
    private bool _disposed;

    public bool IsInstalled => File.Exists(_pythonPath) && ReadRuntimeDevice() is "cpu" or "gpu";

    public bool SupportsGpu => File.Exists(_pythonPath) && ReadRuntimeDevice() == "gpu";

    public bool IsDeviceReady(string device) => device switch
    {
        CpuDevice => IsInstalled,
        GpuDevice => SupportsGpu,
        _ => false,
    };

    public bool KeepLoaded
    {
        get => _keepLoaded;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_keepLoaded == value) return;
            _keepLoaded = value;
            if (!value) StopPersistentWorker();
        }
    }

    public async Task SetupAsync(string device, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!SupportedDevices.Contains(device)) throw new ArgumentOutOfRangeException(nameof(device));
        StopPersistentWorker();
        string script = ResolveBundledFile("scripts", "Setup-Ocr.ps1");
        ProcessStartInfo startInfo = new("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("-Device");
        startInfo.ArgumentList.Add(device == GpuDevice ? "gpu" : "cpu");

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the OCR setup process.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string standardOutput = await output;
        string standardError = await error;
        if (process.ExitCode != 0 || !IsDeviceReady(device))
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(standardError) ? standardOutput.Trim() : standardError.Trim());
        }
    }

    public async Task<OcrWorkerResult> RunAsync(
        string imagePath,
        PixelRect crop,
        string modelName,
        string device,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!SupportedModels.Contains(modelName)) throw new ArgumentOutOfRangeException(nameof(modelName));
        if (!SupportedDevices.Contains(device)) throw new ArgumentOutOfRangeException(nameof(device));
        if (!IsDeviceReady(device)) throw new InvalidOperationException($"OCR {device} is not set up yet.");

        string temporaryRoot = Path.Combine(Path.GetTempPath(), "LilacMacro", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        string cropPath = Path.Combine(temporaryRoot, "crop.png");
        string outputPath = Path.Combine(temporaryRoot, "result.json");
        try
        {
            await Application.Current.Dispatcher.InvokeAsync(() => WriteCrop(imagePath, crop, cropPath));
            OcrWorkerResult result = KeepLoaded
                ? await RunPersistentAsync(cropPath, modelName, device, cancellationToken)
                : await RunOneShotAsync(cropPath, outputPath, modelName, device, cancellationToken);
            return OffsetRegions(result, crop);
        }
        finally
        {
            TryDeleteTemporaryDirectory(temporaryRoot);
        }
    }

    private async Task<OcrWorkerResult> RunOneShotAsync(
        string cropPath,
        string outputPath,
        string modelName,
        string device,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = CreateWorkerStartInfo();
        startInfo.ArgumentList.Add("--input");
        startInfo.ArgumentList.Add(cropPath);
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(modelName);
        startInfo.ArgumentList.Add("--device");
        startInfo.ArgumentList.Add(device);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the OCR worker.");
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
        string standardError;
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            _ = await output;
            standardError = await error;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new TimeoutException("OCR worker did not finish within 2 minutes.");
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidOperationException($"OCR worker failed: {Tail(standardError).Trim()}");
        }

        FileInfo resultFile = new(outputPath);
        if (resultFile.Length > 1024 * 1024) throw new InvalidDataException("OCR result exceeded the safe size limit.");
        await using FileStream stream = File.OpenRead(outputPath);
        return await JsonSerializer.DeserializeAsync<OcrWorkerResult>(stream, cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("OCR worker returned an empty result.");
    }

    private async Task<OcrWorkerResult> RunPersistentAsync(
        string cropPath,
        string modelName,
        string device,
        CancellationToken cancellationToken)
    {
        await _workerGate.WaitAsync(cancellationToken);
        try
        {
            Process process = EnsurePersistentWorker();
            string requestId = Guid.NewGuid().ToString("N");
            string channel = _persistentChannel!;
            string requestPath = Path.Combine(channel, $"request-{requestId}.json");
            string responsePath = Path.Combine(channel, $"response-{requestId}.json");
            string request = JsonSerializer.Serialize(new
            {
                request_id = requestId,
                input = cropPath,
                model = modelName,
                device,
            });
            string temporaryRequest = requestPath + ".tmp";
            await File.WriteAllTextAsync(temporaryRequest, request, new UTF8Encoding(false), cancellationToken);
            File.Move(temporaryRequest, requestPath);

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            try
            {
                while (!File.Exists(responsePath))
                {
                    if (process.HasExited)
                    {
                        throw new InvalidOperationException($"OCR worker stopped unexpectedly. {ReadErrorTail()}");
                    }
                    await Task.Delay(25, timeout.Token);
                }

                FileInfo resultFile = new(responsePath);
                if (resultFile.Length > 1024 * 1024) throw new InvalidDataException("OCR result exceeded the safe size limit.");
                string json = await File.ReadAllTextAsync(responsePath, timeout.Token);
                using JsonDocument document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("error", out JsonElement error))
                {
                    throw new InvalidOperationException($"OCR worker failed: {error.GetString()}");
                }
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
            StopPersistentWorker();
            throw;
        }
        finally
        {
            _workerGate.Release();
        }
    }

    private Process EnsurePersistentWorker()
    {
        if (_persistentWorker is { HasExited: false } running) return running;
        StopPersistentWorker();
        lock (_errorLock) _errorTail.Clear();
        string channel = Path.Combine(Path.GetTempPath(), "LilacMacro", $"worker-{Guid.NewGuid():N}");
        Directory.CreateDirectory(channel);
        ProcessStartInfo startInfo = CreateWorkerStartInfo();
        startInfo.ArgumentList.Add("--serve");
        startInfo.ArgumentList.Add("--channel");
        startInfo.ArgumentList.Add(channel);
        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the persistent OCR worker.");
        process.ErrorDataReceived += Worker_OnErrorDataReceived;
        process.OutputDataReceived += Worker_OnOutputDataReceived;
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        _persistentWorker = process;
        _persistentChannel = channel;
        return process;
    }

    private ProcessStartInfo CreateWorkerStartInfo()
    {
        string worker = ResolveBundledFile("tools", "ocr_worker.py");
        ProcessStartInfo startInfo = new(_pythonPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add(worker);
        startInfo.Environment["PADDLE_PDX_MODEL_SOURCE"] = "BOS";
        startInfo.Environment["PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK"] = "True";
        return startInfo;
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
        // Paddle writes native runtime diagnostics to stdout; draining prevents a full pipe from stalling inference.
    }

    private string ReadErrorTail()
    {
        lock (_errorLock) return _errorTail.ToString().Trim();
    }

    private string? ReadRuntimeDevice()
    {
        try
        {
            return File.Exists(_runtimeMarkerPath)
                ? File.ReadAllText(_runtimeMarkerPath).Trim().ToLowerInvariant()
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void StopPersistentWorker()
    {
        Process? process = _persistentWorker;
        string? channel = _persistentChannel;
        _persistentWorker = null;
        _persistentChannel = null;
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

    private static OcrWorkerResult OffsetRegions(OcrWorkerResult result, PixelRect crop) => result with
    {
        Regions = result.Regions.Select(region => region with
        {
            X = checked(region.X + crop.X),
            Y = checked(region.Y + crop.Y),
        }).ToArray(),
    };

    private static void WriteCrop(string imagePath, PixelRect crop, string destination)
    {
        BitmapImage source = new();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.UriSource = new Uri(imagePath, UriKind.Absolute);
        source.EndInit();
        source.Freeze();
        PixelSize size = new(source.PixelWidth, source.PixelHeight);
        if (!crop.IsInside(size)) throw new ArgumentOutOfRangeException(nameof(crop));

        CroppedBitmap cropped = new(source, new Int32Rect(crop.X, crop.Y, crop.Width, crop.Height));
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(cropped));
        using FileStream stream = File.Create(destination);
        encoder.Save(stream);
    }

    private static string ResolveBundledFile(string directory, string fileName)
    {
        string bundled = Path.Combine(AppContext.BaseDirectory, directory, fileName);
        if (File.Exists(bundled)) return bundled;
        string development = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", directory, fileName));
        return File.Exists(development)
            ? development
            : throw new FileNotFoundException($"LilacMacro could not locate {fileName}.");
    }

    private static string Tail(string value) => value.Length > 1200 ? value[^1200..] : value;

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
            // The OS temp cleaner can remove a transient crop left by a locked decoder.
        }
        catch (UnauthorizedAccessException)
        {
            // The OS temp cleaner can remove a transient crop left by a locked decoder.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopPersistentWorker();
        _workerGate.Dispose();
    }
}
