using LilacMacro.Windows.LocalSession;

namespace LilacMacro.Tests;

public sealed class LocalSessionUpdateRepairTests
{
    [Theory]
    [InlineData(false, false, 0, true)]
    [InlineData(false, true, 0, true)]
    [InlineData(true, false, 0, true)]
    [InlineData(true, true, 1, true)]
    [InlineData(true, true, 0, false)]
    public void Repair_preserves_connected_rdp_sessions_only_when_service_state_is_exact(
        bool repair,
        bool serviceRunning,
        int mismatchCount,
        bool expectedRestart)
    {
        string[] mismatches = Enumerable.Range(0, mismatchCount)
            .Select(index => $"mismatch-{index}")
            .ToArray();

        Assert.Equal(
            expectedRestart,
            LocalSessionProvisioner.ShouldRestartTermService(repair, serviceRunning, mismatches));
    }
}
