using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LilacMacro.Runtime.Services;

namespace LilacMacro.App.Infrastructure;

public sealed partial class OcrRunner
{
    public async Task<OcrGpuInfo?> ProbeGpuAsync(CancellationToken cancellationToken = default)
    {
        ProcessStartInfo startInfo = CreateSetupStartInfo("probe");
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the OCR GPU probe.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            string json = await output.ConfigureAwait(false);
            _ = await error.ConfigureAwait(false);
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<OcrGpuInfo>(json.Trim(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
            try { await Task.WhenAll(output, error).ConfigureAwait(false); }
            catch (Exception) { }
            throw;
        }
    }

    public async Task SetupAsync(
        string device,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!SupportedDevices.Contains(device)) throw new ArgumentOutOfRangeException(nameof(device));
        if (device == CpuDevice && IsDeviceReady(CpuDevice))
        {
            progress?.Report("CPU OCR is bundled and ready.");
            if (KeepLoaded) await WarmUpAsync(SmallModel, device, cancellationToken);
            return;
        }
        if (device == GpuDevice && IsDeviceReady(GpuDevice))
        {
            progress?.Report("GPU OCR is already ready.");
            if (KeepLoaded) await WarmUpAsync(SmallModel, device, cancellationToken);
            return;
        }
        Stopwatch setupTimer = Stopwatch.StartNew();
        int? processExitCode = null;
        try
        {
            _persistentWorker.Stop();
            progress?.Report(device == GpuDevice
                ? "Preparing the per-user GPU OCR environment."
                : "Preparing the local OCR environment.");
            ProcessStartInfo startInfo = CreateSetupStartInfo(device);

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start the OCR setup process.");
            Task<string> output = DrainSetupStreamAsync(process.StandardOutput, progress, cancellationToken);
            Task<string> error = DrainSetupStreamAsync(process.StandardError, progress, cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { }
                }
                try { await Task.WhenAll(output, error).ConfigureAwait(false); }
                catch (Exception) { }
                throw;
            }
            string standardOutput = await output;
            string standardError = await error;
            if (process.ExitCode != 0 || !IsDeviceReady(device))
            {
                processExitCode = process.ExitCode;
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(standardError) ? standardOutput.Trim() : standardError.Trim());
            }
            if (KeepLoaded) await WarmUpAsync(SmallModel, device, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error) when (error is not ArgumentOutOfRangeException and not ObjectDisposedException)
        {
            string code = OcrSetupFailurePolicy.Classify(error.Message, device, processExitCode);
            _deepDebug.RecordEvent("ocr_setup", "setup_failed", new OcrSetupFailureObservation(
                device,
                code,
                OcrSetupFailurePolicy.Stage(code),
                OcrSetupFailurePolicy.BoundedDuration(setupTimer.Elapsed),
                OcrSetupFailurePolicy.BoundedExitCode(processExitCode),
                OcrSetupFailurePolicy.IsCommandAvailableOnPath("py.exe"),
                OcrSetupFailurePolicy.IsCommandAvailableOnPath("winget.exe"),
                File.Exists(_pythonPath) || BundledPythonPath() is not null,
                File.Exists(_runtimeMarkerPath) || File.Exists(_gpuRuntimeMarkerPath)));
            throw;
        }
    }

    private ProcessStartInfo CreateSetupStartInfo(string operation)
    {
        string script = ResolveBundledFile("scripts", "Setup-Ocr.ps1");
        ProcessStartInfo startInfo = new("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        if (operation == "probe")
        {
            startInfo.ArgumentList.Add("-ProbeGpu");
            return startInfo;
        }
        startInfo.ArgumentList.Add("-Device");
        startInfo.ArgumentList.Add(operation == GpuDevice ? "gpu" : "cpu");
        startInfo.ArgumentList.Add("-InstallRoot");
        startInfo.ArgumentList.Add(_ocrRoot);
        string? bundledPython = BundledPythonPath();
        if (bundledPython is not null)
        {
            startInfo.ArgumentList.Add("-BundledPythonPath");
            startInfo.ArgumentList.Add(bundledPython);
        }
        return startInfo;
    }

    private static async Task<string> DrainSetupStreamAsync(
        StreamReader reader,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        StringBuilder text = new();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (text.Length < 12000)
            {
                if (text.Length > 0) text.AppendLine();
                text.Append(line);
            }
            if (!string.IsNullOrWhiteSpace(line)) progress?.Report(line);
        }
        return text.ToString();
    }

    private string? RuntimePythonPath(string? device)
    {
        if (device == CpuDevice && BundledCpuReady()) return BundledPythonPath();
        if (device == GpuDevice && File.Exists(Path.Combine(_gpuRoot, "venv", "Scripts", "python.exe"))
            && ReadMarker(_gpuRuntimeMarkerPath) == "gpu")
            return Path.Combine(_gpuRoot, "venv", "Scripts", "python.exe");
        return File.Exists(_pythonPath) && ReadRuntimeDevice() == device ? _pythonPath : null;
    }

    private string? BundledPythonPath()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "ocr", "python", "python.exe");
        return File.Exists(path) ? path : null;
    }

    private bool BundledCpuReady() =>
        BundledPythonPath() is not null
        && File.Exists(Path.Combine(AppContext.BaseDirectory, "ocr", "cpu-runtime.json"));

    private string? ReadRuntimeDevice() => ReadMarker(_runtimeMarkerPath);

    private static string? ReadMarker(string path)
    {
        try
        {
            return File.Exists(path)
                ? File.ReadAllText(path).Trim().ToLowerInvariant()
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
}
