using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LilacMacro.Core.Geometry;
using LilacMacro.App.Diagnostics;

namespace LilacMacro.App.Infrastructure;

public sealed class OcrRunner : IDisposable
{
    public const string SmallModel = "PP-OCRv6_small_rec";
    public const string TinyModel = "PP-OCRv6_tiny_rec";
    public const string CpuDevice = "cpu";
    public const string GpuDevice = "gpu:0";
    private static readonly HashSet<string> SupportedModels = [SmallModel, TinyModel];
    private static readonly HashSet<string> SupportedDevices = [CpuDevice, GpuDevice];
    private readonly string _ocrRoot;
    private readonly string _pythonPath;
    private readonly string _runtimeMarkerPath;
    private readonly PersistentOcrWorker _persistentWorker;
    private bool _keepLoaded;
    private bool _disposed;
    private readonly DeepDebugSessionService _deepDebug;

    public OcrRunner(DeepDebugSessionService deepDebug, string? ocrRoot = null)
    {
        _deepDebug = deepDebug;
        _ocrRoot = Path.GetFullPath(ocrRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LilacMacro",
            "ocr"));
        _pythonPath = Path.Combine(_ocrRoot, "venv", "Scripts", "python.exe");
        _runtimeMarkerPath = Path.Combine(_ocrRoot, "runtime-device.txt");
        _persistentWorker = new PersistentOcrWorker(CreateWorkerStartInfo);
    }

    public bool IsInstalled => File.Exists(_pythonPath) && ReadRuntimeDevice() is "cpu" or "gpu";

    public bool SupportsGpu => File.Exists(_pythonPath) && ReadRuntimeDevice() == "gpu";

    public bool IsDeviceReady(string device) => device switch
    {
        CpuDevice => IsInstalled,
        GpuDevice => SupportsGpu,
        _ => false,
    };

    public async Task<string> EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        string? readyDevice = SelectPreferredDevice(IsDeviceReady(GpuDevice), IsDeviceReady(CpuDevice));
        if (readyDevice is not null) return readyDevice;

        await SetupAsync(CpuDevice, cancellationToken).ConfigureAwait(false);
        return CpuDevice;
    }

    internal static string? SelectPreferredDevice(bool gpuReady, bool cpuReady) =>
        gpuReady ? GpuDevice : cpuReady ? CpuDevice : null;

    public bool KeepLoaded
    {
        get => _keepLoaded;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_keepLoaded == value) return;
            _keepLoaded = value;
            if (!value) _persistentWorker.Stop();
        }
    }

    public async Task SetupAsync(string device, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!SupportedDevices.Contains(device)) throw new ArgumentOutOfRangeException(nameof(device));
        _persistentWorker.Stop();
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
        startInfo.ArgumentList.Add("-InstallRoot");
        startInfo.ArgumentList.Add(_ocrRoot);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the OCR setup process.");
        string standardOutput;
        string standardError;
        try
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            standardOutput = await output;
            standardError = await error;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
            throw;
        }
        if (process.ExitCode != 0 || !IsDeviceReady(device))
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(standardError) ? standardOutput.Trim() : standardError.Trim());
        }
        if (KeepLoaded) await WarmUpAsync(SmallModel, device, cancellationToken);
    }

    public async Task WarmUpAsync(
        string modelName,
        string device,
        CancellationToken cancellationToken = default,
        int scale = 1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!KeepLoaded) throw new InvalidOperationException("OCR preload requires Keep Loaded.");
        if (!SupportedModels.Contains(modelName)) throw new ArgumentOutOfRangeException(nameof(modelName));
        if (!SupportedDevices.Contains(device)) throw new ArgumentOutOfRangeException(nameof(device));
        if (!IsDeviceReady(device)) throw new InvalidOperationException($"OCR {device} is not set up yet.");
        await _persistentWorker.WarmUpAsync(modelName, device, cancellationToken);
        _deepDebug.RecordEvent("ocr", "worker_preloaded", new { Model = modelName, Device = device });
    }

    public async Task<OcrWorkerResult> RunAsync(
        string imagePath,
        PixelRect crop,
        string modelName,
        string device,
        CancellationToken cancellationToken = default,
        int scale = 1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!SupportedModels.Contains(modelName)) throw new ArgumentOutOfRangeException(nameof(modelName));
        if (!SupportedDevices.Contains(device)) throw new ArgumentOutOfRangeException(nameof(device));
        if (scale is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(scale));
        if (!IsDeviceReady(device)) throw new InvalidOperationException($"OCR {device} is not set up yet.");

        DeepDebugScope? scope = _deepDebug.IsActive
            ? null
            : await _deepDebug.OpenSessionAsync(
                "ocr-test",
                new DeepDebugOperationContext(
                    "dataset-builder",
                    new { imagePath, crop, modelName, device, KeepLoaded }));
        string temporaryRoot = Path.Combine(Path.GetTempPath(), "LilacMacro", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        string cropPath = Path.Combine(temporaryRoot, "crop.png");
        string outputPath = Path.Combine(temporaryRoot, "result.json");
        try
        {
            OcrWorkerResult result = KeepLoaded
                ? await RunPersistentWithAccessRecoveryAsync(
                    imagePath, crop, cropPath, modelName, device, cancellationToken, scale)
                : await RunOneShotAsync(imagePath, crop, cropPath, outputPath, modelName, device, cancellationToken, scale);
            _deepDebug.RecordPng(await File.ReadAllBytesAsync(cropPath, cancellationToken), "ocr-crop", new
            {
                Source = imagePath,
                Crop = crop,
                Model = modelName,
                Device = device,
                KeepLoaded,
            });
            OcrWorkerResult offset = OffsetRegions(result, crop, scale);
            _deepDebug.RecordEvent("ocr", "inference_completed", new
            {
                offset.ModelName,
                offset.DetectorModelName,
                offset.Device,
                offset.ModelLoadMilliseconds,
                offset.InferenceMilliseconds,
                offset.ModelCached,
                offset.PaddleOcrVersion,
                Crop = crop,
                offset.Regions,
                offset.Text,
                offset.Confidence,
            });
            if (scope is not null) await scope.CompleteAsync("success");
            return offset;
        }
        catch (OperationCanceledException error)
        {
            if (scope is not null) await scope.CompleteAsync("canceled", error);
            throw;
        }
        catch (Exception error)
        {
            _deepDebug.RecordEvent("ocr", "inference_failed", new { Error = error.ToString() });
            if (scope is not null) await scope.CompleteAsync("error", error);
            throw;
        }
        finally
        {
            TryDeleteTemporaryDirectory(temporaryRoot);
        }
    }

    private async Task<OcrWorkerResult> RunPersistentWithAccessRecoveryAsync(
        string imagePath,
        PixelRect crop,
        string cropPath,
        string modelName,
        string device,
        CancellationToken cancellationToken,
        int scale)
    {
        const int maximumAttempts = 4;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await _persistentWorker.RunAsync(
                    imagePath, crop, cropPath, modelName, device, cancellationToken, scale).ConfigureAwait(false);
            }
            catch (Exception error) when (
                IsTransientWorkerAccessFailure(error) && attempt < maximumAttempts)
            {
                _deepDebug.RecordEvent("ocr", "worker_access_retry", new
                {
                    Attempt = attempt,
                    MaximumAttempts = maximumAttempts,
                    Error = error.Message,
                });
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    internal static bool IsTransientWorkerAccessFailure(Exception error) =>
        error is OcrWorkerResponseAccessException ||
        (error.Message.Contains("OCR worker failed", StringComparison.OrdinalIgnoreCase) &&
         (error.Message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
          error.Message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)));

    private async Task<OcrWorkerResult> RunOneShotAsync(
        string imagePath,
        PixelRect crop,
        string cropPath,
        string outputPath,
        string modelName,
        string device,
        CancellationToken cancellationToken,
        int scale)
    {
        ProcessStartInfo startInfo = CreateWorkerStartInfo();
        startInfo.ArgumentList.Add("--input");
        startInfo.ArgumentList.Add(imagePath);
        startInfo.ArgumentList.Add("--crop");
        startInfo.ArgumentList.Add(crop.X.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(crop.Y.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(crop.Width.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(crop.Height.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--crop-output");
        startInfo.ArgumentList.Add(cropPath);
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(modelName);
        startInfo.ArgumentList.Add("--device");
        startInfo.ArgumentList.Add(device);
        startInfo.ArgumentList.Add("--scale");
        startInfo.ArgumentList.Add(scale.ToString(System.Globalization.CultureInfo.InvariantCulture));
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

    private static OcrWorkerResult OffsetRegions(OcrWorkerResult result, PixelRect crop, int scale) => result with
    {
        Regions = result.Regions.Select(region => region with
        {
            X = checked((int)Math.Round(region.X / (double)scale) + crop.X),
            Y = checked((int)Math.Round(region.Y / (double)scale) + crop.Y),
            Width = Math.Max(1, checked((int)Math.Round(region.Width / (double)scale))),
            Height = Math.Max(1, checked((int)Math.Round(region.Height / (double)scale))),
        }).ToArray(),
    };

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
        _persistentWorker.Dispose();
    }
}
