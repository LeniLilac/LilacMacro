using LilacMacro.Windows;

namespace LilacMacro.Tests;

public sealed class WindowsSecretAndLaunchTests
{
    [Fact]
    public void DpapiRoundTripsForCurrentWindowsUser()
    {
        DpapiSecretProtector protector = new();
        string plaintext = $"secret-{Guid.NewGuid():N}";

        string encrypted = protector.Protect(plaintext);

        Assert.NotEqual(plaintext, encrypted);
        Assert.Equal(plaintext, protector.Unprotect(encrypted));
    }

    [Fact]
    public void DpapiRoundTripsForAclRestrictedMachineConfiguration()
    {
        DpapiSecretProtector protector = new(machineScope: true);
        string plaintext = $"shared-{Guid.NewGuid():N}";

        string encrypted = protector.Protect(plaintext);

        Assert.NotEqual(plaintext, encrypted);
        Assert.Equal(plaintext, protector.Unprotect(encrypted));
    }

    [Fact]
    public async Task RobloxLauncherRejectsWebUrlsBeforeShellExecution()
    {
        RobloxProtocolLauncher launcher = new();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            launcher.LaunchAsync(new Uri("https://www.roblox.com/share?code=secret&type=Server"), CancellationToken.None));
    }
}
