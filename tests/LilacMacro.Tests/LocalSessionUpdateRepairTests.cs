using LilacMacro.Windows.LocalSession;

namespace LilacMacro.Tests;

public sealed class LocalSessionUpdateRepairTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void Repair_preserves_connected_rdp_sessions_only_when_service_state_is_exact(
        bool repair,
        bool serviceRunning,
        bool expectedRestart)
    {
        Assert.Equal(
            expectedRestart,
            LocalSessionProvisioner.ShouldRestartTermService(repair, serviceRunning));
    }
}
