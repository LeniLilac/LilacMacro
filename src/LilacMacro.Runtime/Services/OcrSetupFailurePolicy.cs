namespace LilacMacro.Runtime.Services;

internal sealed record OcrSetupFailureObservation(
    string Device,
    string FailureCode,
    string SetupStage,
    int DurationMilliseconds,
    int? ProcessExitCode,
    bool PythonLauncherPresent,
    bool WingetPresent,
    bool ExistingOcrPythonPresent,
    bool RuntimeMarkerPresent);

internal static class OcrSetupFailurePolicy
{
    internal static string Classify(string message, string device, int? processExitCode)
    {
        string text = message.Trim().ToLowerInvariant();
        if (text.Contains("could not find its bundled python 3.12 runtime", StringComparison.Ordinal))
            return "python312_missing";
        if (text.Contains("no suitable python runtime found", StringComparison.Ordinal))
            return "python312_missing";
        if (text.Contains("windows app installer is unavailable", StringComparison.Ordinal))
            return "winget_unavailable";
        if (text.Contains("could not automatically install python 3.12", StringComparison.Ordinal))
            return "python_install_failed";
        if (text.Contains("installed python 3.12 but could not locate", StringComparison.Ordinal))
            return "python312_not_found";
        if (device == "gpu:0" && text.Contains("compute capability", StringComparison.Ordinal))
            return "gpu_runtime_invalid";
        if (device == "gpu:0" && (text.Contains("nvidia", StringComparison.Ordinal)
            || text.Contains("gpu query", StringComparison.Ordinal)))
            return "gpu_detection_failed";
        if (text.Contains("could not create the lilacmacro ocr environment", StringComparison.Ordinal))
            return "venv_create_failed";
        if (text.Contains("could not update pip", StringComparison.Ordinal))
            return "pip_update_failed";
        if (text.Contains("paddlepaddle", StringComparison.Ordinal))
            return "paddle_install_failed";
        if (text.Contains("paddleocr", StringComparison.Ordinal))
            return "paddleocr_install_failed";
        if (text.Contains("import check", StringComparison.Ordinal))
            return "ocr_import_failed";
        if (processExitCode == 0) return "runtime_not_ready";
        if (text.Contains("could not start the ocr setup process", StringComparison.Ordinal))
            return "setup_process_start_failed";
        if (processExitCode is not null) return "setup_process_failed";
        return "setup_failed";
    }

    internal static string Stage(string code) => code switch
    {
        "python312_missing" or "winget_unavailable" or "python_install_failed" or "python312_not_found"
            => "python-bootstrap",
        "gpu_detection_failed" or "gpu_runtime_invalid" => "gpu-runtime",
        "venv_create_failed" => "environment",
        "pip_update_failed" or "paddle_install_failed" => "paddle",
        "paddleocr_install_failed" => "paddleocr",
        "ocr_import_failed" => "import-check",
        "runtime_not_ready" => "runtime",
        "setup_process_start_failed" or "setup_process_failed" => "process",
        _ => "setup",
    };

    internal static int BoundedDuration(TimeSpan elapsed) =>
        (int)Math.Clamp(elapsed.TotalMilliseconds, 0, 600_000);

    internal static int? BoundedExitCode(int? exitCode) =>
        exitCode is >= 0 and <= 65_535 ? exitCode : null;

    internal static bool IsCommandAvailableOnPath(string command)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return false;
        foreach (string rawDirectory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string directory = rawDirectory.Trim().Trim('"');
            try
            {
                if (directory.Length > 0 && File.Exists(Path.Combine(directory, command))) return true;
            }
            catch (ArgumentException)
            {
                // An invalid PATH entry is not a usable runtime flag.
            }
            catch (IOException)
            {
                // A transient PATH entry failure is not a usable runtime flag.
            }
        }
        return false;
    }
}
