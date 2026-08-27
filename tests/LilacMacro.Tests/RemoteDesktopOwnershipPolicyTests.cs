using LilacMacro.Windows.LocalSession;

namespace LilacMacro.Tests;

public sealed class RemoteDesktopOwnershipPolicyTests
{
    private const string WindowsServiceDll = @"C:\Windows\System32\termsrv.dll";

    [Fact]
    public void Fresh_install_accepts_disabled_native_listener()
    {
        IReadOnlyList<string> problems = RemoteDesktopOwnershipPolicy.EvaluateFreshInstall(
            new RemoteDesktopConfigurationObservation(1, 3389, WindowsServiceDll, false),
            WindowsServiceDll);

        Assert.Empty(problems);
    }

    [Theory]
    [InlineData(0, 3389, @"C:\Windows\System32\termsrv.dll", false)]
    [InlineData(1, 33991, @"C:\Windows\System32\termsrv.dll", false)]
    [InlineData(1, 3389, @"C:\Tools\RdpWrap.dll", false)]
    [InlineData(1, 3389, @"C:\Windows\System32\termsrv.dll", true)]
    public void Fresh_install_rejects_preexisting_rdp_ownership(
        int denyConnections,
        int port,
        string serviceDll,
        bool portInUse)
    {
        IReadOnlyList<string> problems = RemoteDesktopOwnershipPolicy.EvaluateFreshInstall(
            new RemoteDesktopConfigurationObservation(denyConnections, port, serviceDll, portInUse),
            WindowsServiceDll);

        string problem = Assert.Single(problems);
        Assert.StartsWith(RemoteDesktopOwnershipPolicy.ConflictPrefix, problem, StringComparison.Ordinal);
        Assert.Contains("run LilacMacro normally inside an existing RDP session", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_configuration_rejects_external_drift_instead_of_repairing_it()
    {
        IReadOnlyList<string> problems = RemoteDesktopOwnershipPolicy.EvaluateManagedConfiguration(
            ["Registry value differs from the owned configuration: ServiceDll"]);

        Assert.True(RemoteDesktopOwnershipPolicy.IsOwnershipConflict(problems));
        Assert.Contains("will not overwrite it", problems[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, 2, 2, 0)]
    [InlineData(true, 0, 2, 1)]
    [InlineData(true, 2, 0, 2)]
    public void Cleanup_accepts_only_unowned_owned_or_already_restored_state(
        bool mutationStarted,
        int ownedMismatchCount,
        int originalMismatchCount,
        int expected)
    {
        Assert.Equal((RemoteDesktopCleanupDisposition)expected, RemoteDesktopOwnershipPolicy.EvaluateCleanup(
            mutationStarted,
            Enumerable.Repeat("owned mismatch", ownedMismatchCount).ToArray(),
            Enumerable.Repeat("original mismatch", originalMismatchCount).ToArray()));
    }

    [Fact]
    public void Cleanup_rejects_third_party_configuration_without_overwriting_it()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            RemoteDesktopOwnershipPolicy.EvaluateCleanup(true, ["owned mismatch"], ["original mismatch"]));

        Assert.Contains("will not overwrite it", error.Message, StringComparison.Ordinal);
    }
}
