using LilacMacro.App.Diagnostics;
using LilacMacro.App.Infrastructure;

namespace LilacMacro.Tests;

public sealed class OcrSetupTests
{
    [Theory]
    [InlineData(true, true, OcrRunner.GpuDevice)]
    [InlineData(false, true, OcrRunner.CpuDevice)]
    [InlineData(false, false, null)]
    public void PreferredDeviceUsesReadyRuntime(bool gpuReady, bool cpuReady, string? expected) =>
        Assert.Equal(expected, OcrRunner.SelectPreferredDevice(gpuReady, cpuReady));

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
}
