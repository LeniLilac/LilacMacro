using LilacMacro.App.Workspace;

namespace LilacMacro.Tests;

public sealed class CrossProcessRobloxInputGateTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"lilac-input-gate-{Guid.NewGuid():N}");

    [Fact]
    public void Acquire_WhileAnotherLeaseIsActiveFailsClosed()
    {
        string path = Path.Combine(_directory, "roblox-input.lock");
        CrossProcessRobloxInputGate firstGate = new(path);
        CrossProcessRobloxInputGate secondGate = new(path);

        using IDisposable lease = firstGate.Acquire();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(secondGate.Acquire);
        Assert.Contains("owns Roblox input", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Acquire_AfterLeaseIsReleasedSucceeds()
    {
        string path = Path.Combine(_directory, "roblox-input.lock");
        CrossProcessRobloxInputGate gate = new(path);
        gate.Acquire().Dispose();

        using IDisposable lease = gate.Acquire();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
