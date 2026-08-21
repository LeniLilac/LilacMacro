using LilacMacro.App.Infrastructure;

namespace LilacMacro.Tests;

public sealed class MacroInstanceContextTests
{
    [Fact]
    public void Provisioning_owner_uses_the_existing_shared_configuration()
    {
        Assert.True(MacroInstanceContext.ShouldUseSharedConfiguration(
            sharedDirectoryExists: true,
            provisionedOwnerSid: "S-1-5-21-1000",
            currentSid: "S-1-5-21-1000"));
    }

    [Fact]
    public void Unrelated_windows_user_uses_profile_local_configuration()
    {
        Assert.False(MacroInstanceContext.ShouldUseSharedConfiguration(
            sharedDirectoryExists: true,
            provisionedOwnerSid: "S-1-5-21-1000",
            currentSid: "S-1-5-21-1001"));
    }

    [Theory]
    [InlineData(false, "S-1-5-21-1000", "S-1-5-21-1000")]
    [InlineData(true, null, "S-1-5-21-1000")]
    [InlineData(true, "S-1-5-21-1000", null)]
    public void Shared_configuration_selection_fails_closed(
        bool sharedDirectoryExists,
        string? provisionedOwnerSid,
        string? currentSid)
    {
        Assert.False(MacroInstanceContext.ShouldUseSharedConfiguration(
            sharedDirectoryExists,
            provisionedOwnerSid,
            currentSid));
    }

    [Theory]
    [InlineData(true, true, "machine")]
    [InlineData(true, false, "local")]
    [InlineData(false, true, "local")]
    [InlineData(false, false, "local")]
    public void Diagnostics_use_machine_storage_only_when_provisioned_and_ready(
        bool preferMachineStorage,
        bool machineDirectoryReady,
        string expected)
    {
        Assert.Equal(expected, MacroInstanceContext.SelectDiagnosticsRoot(
            "local",
            "machine",
            preferMachineStorage,
            machineDirectoryReady));
    }
}
