using LilacMacro.App.Diagnostics;
using LilacMacro.App.Infrastructure;
using LilacMacro.Core.Services;
using LilacMacro.Core.Ocr;
using LilacMacro.Runtime.Services;
using System.Diagnostics;

namespace LilacMacro.Tests;

public sealed class OcrSetupTests
{
    [Theory]
    [InlineData(true, false, OcrExecutionMode.Automatic, true)]
    [InlineData(false, false, OcrExecutionMode.Automatic, false)]
    [InlineData(true, true, OcrExecutionMode.GpuPreferred, true)]
    [InlineData(false, true, OcrExecutionMode.Automatic, true)]
    [InlineData(true, true, OcrExecutionMode.CpuOnly, false)]
    public void Managed_instances_check_their_profile_local_gpu_runtime(
        bool acceptedPrivacyThisLaunch,
        bool isManagedRunner,
        OcrExecutionMode mode,
        bool expected)
    {
        Assert.Equal(
            expected,
            global::LilacMacro.App.App.ShouldCheckGpuSetup(acceptedPrivacyThisLaunch, isManagedRunner, mode));
    }

    [Theory]
    [InlineData(true, true, OcrRunner.GpuDevice)]
    [InlineData(false, true, OcrRunner.CpuDevice)]
    [InlineData(false, false, null)]
    public void PreferredDeviceUsesReadyRuntime(bool gpuReady, bool cpuReady, string? expected) =>
        Assert.Equal(expected, OcrRunner.SelectPreferredDevice(gpuReady, cpuReady));

    [Theory]
    [InlineData(OcrExecutionMode.Automatic, true, true, OcrRunner.GpuDevice)]
    [InlineData(OcrExecutionMode.Automatic, false, true, OcrRunner.CpuDevice)]
    [InlineData(OcrExecutionMode.GpuPreferred, false, true, null)]
    [InlineData(OcrExecutionMode.GpuPreferred, true, true, OcrRunner.GpuDevice)]
    [InlineData(OcrExecutionMode.CpuOnly, true, true, OcrRunner.CpuDevice)]
    public void Selected_mode_owns_runtime_selection(
        OcrExecutionMode mode,
        bool gpuReady,
        bool cpuReady,
        string? expected) =>
        Assert.Equal(expected, OcrRunner.SelectDevice(mode, gpuReady, cpuReady));

    [Fact]
    public void Run_device_policy_retries_gpu_once_then_uses_cpu_for_the_run()
    {
        OcrRunDevicePolicy policy = new();
        policy.Begin();

        Assert.Equal(
            OcrGpuFailureDecision.RetryGpu,
            policy.ObserveGpuFailure(cpuReady: true));
        Assert.Equal(OcrRunner.GpuDevice, policy.Resolve(OcrRunner.GpuDevice));
        Assert.Equal(
            OcrGpuFailureDecision.UseCpu,
            policy.ObserveGpuFailure(cpuReady: true));
        Assert.Equal(OcrRunner.CpuDevice, policy.Resolve(OcrRunner.GpuDevice));

        policy.End();
        policy.Begin();
        Assert.Equal(OcrRunner.GpuDevice, policy.Resolve(OcrRunner.GpuDevice));
    }

    [Fact]
    public void Successful_gpu_recovery_restores_the_next_incident_retry()
    {
        OcrRunDevicePolicy policy = new();
        policy.Begin();

        Assert.Equal(
            OcrGpuFailureDecision.RetryGpu,
            policy.ObserveGpuFailure(cpuReady: true));
        policy.ObserveGpuSuccess();

        Assert.Equal(
            OcrGpuFailureDecision.RetryGpu,
            policy.ObserveGpuFailure(cpuReady: true));
    }

    [Fact]
    public void Worker_timeout_is_a_recoverable_gpu_failure() =>
        Assert.True(OcrRunner.IsRecoverableGpuWorkerFailure(
            new OcrWorkerTimeoutException("response", TimeSpan.FromSeconds(30))));

    [Fact]
    public void Persistent_worker_watchdog_is_shorter_than_lobby_deadline() =>
        Assert.Equal(TimeSpan.FromSeconds(30), PersistentOcrWorker.OperationDeadline);

    [Fact]
    public void Persistent_worker_allows_slow_cold_model_loading() =>
        Assert.Equal(TimeSpan.FromMinutes(2), PersistentOcrWorker.ModelLoadingDeadline);

    [Fact]
    public void Bundled_worker_ignores_ambient_python_runtime_paths()
    {
        ProcessStartInfo startInfo = new("python.exe") { UseShellExecute = false };
        startInfo.Environment["PYTHONHOME"] = @"C:\Python313";
        startInfo.Environment["PYTHONPATH"] = @"C:\Python313\Lib";
        startInfo.Environment["PYTHONSTARTUP"] = @"C:\startup.py";
        startInfo.Environment["PYTHONUSERBASE"] = @"C:\PythonUser";

        OcrRunner.ConfigureWorkerEnvironment(startInfo);

        Assert.False(startInfo.Environment.ContainsKey("PYTHONHOME"));
        Assert.False(startInfo.Environment.ContainsKey("PYTHONPATH"));
        Assert.False(startInfo.Environment.ContainsKey("PYTHONSTARTUP"));
        Assert.False(startInfo.Environment.ContainsKey("PYTHONUSERBASE"));
        Assert.Equal("1", startInfo.Environment["PYTHONNOUSERSITE"]);
    }

    [Fact]
    public async Task EnsureReadyUsesExistingCpuRuntimeWithoutReinstalling()
    {
        string root = Path.Combine(Path.GetTempPath(), "LilacMacro.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "venv", "Scripts"));
        File.WriteAllText(Path.Combine(root, "venv", "Scripts", "python.exe"), string.Empty);
        File.WriteAllText(Path.Combine(root, "runtime-device.txt"), OcrRunner.CpuDevice);

        try
        {
            using OcrRunner runner = new(new DeepDebugSessionService(root), root);
            Assert.Equal(OcrRunner.CpuDevice, await runner.EnsureReadyAsync());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BundledCpuWorkerDisablesMklDnnAcceleration()
    {
        string worker = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "ocr_worker.py"));

        Assert.Contains("if device == \"cpu\":", worker);
        Assert.Contains("options[\"enable_mkldnn\"] = False", worker);
    }

    [Fact]
    public void Persistent_worker_reports_bounded_model_and_inference_phases()
    {
        string worker = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "ocr_worker.py"));

        Assert.Contains("write_status(preload_status, \"model-loading\")", worker);
        Assert.Contains("notify(progress, \"crop-ready\")", worker);
        Assert.Contains("notify(progress, \"inference-running\")", worker);
        Assert.Contains("write_status(status_path, \"response-writing\")", worker);
    }

    [Theory]
    [InlineData("LilacMacro could not find its bundled Python 3.12 runtime.", "cpu", null, "python312_missing", "python-bootstrap")]
    [InlineData("LilacMacro could not automatically install Python 3.12 because Windows App Installer is unavailable.", "cpu", null, "winget_unavailable", "python-bootstrap")]
    [InlineData("NVIDIA GPU has compute capability 5.0; current Paddle GPU packages require 6.0 or newer.", "gpu:0", null, "gpu_runtime_invalid", "gpu-runtime")]
    [InlineData("Could not install PaddleOCR.", "cpu", 1, "paddleocr_install_failed", "paddleocr")]
    [InlineData("unclassified setup failure", "cpu", 9, "setup_process_failed", "process")]
    public void SetupFailures_map_to_bounded_codes_and_stages(
        string message,
        string device,
        int? exitCode,
        string expectedCode,
        string expectedStage)
    {
        string code = OcrSetupFailurePolicy.Classify(message, device, exitCode);

        Assert.Equal(expectedCode, code);
        Assert.Equal(expectedStage, OcrSetupFailurePolicy.Stage(code));
        Assert.True(ProductTelemetryPolicy.IsOcrSetupFailureCode(code));
        Assert.True(ProductTelemetryPolicy.IsOcrSetupStage(OcrSetupFailurePolicy.Stage(code)));
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "eng", "runtime-evidence.json")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the LilacMacro repository root.");
    }
}
