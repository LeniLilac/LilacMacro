using System.ComponentModel;
using System.Diagnostics;

namespace LilacMacro.Windows;

public sealed class RobloxProtocolLauncher
{
    public Task LaunchAsync(Uri launchUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launchUri);
        cancellationToken.ThrowIfCancellationRequested();
        if (!launchUri.IsAbsoluteUri ||
            !launchUri.Scheme.Equals("roblox", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only the registered Roblox URI protocol can be launched.", nameof(launchUri));
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = launchUri.AbsoluteUri,
                UseShellExecute = true,
            });
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException(
                "Windows could not launch Roblox. Reinstall Roblox or restore its roblox:// protocol registration.",
                error);
        }
        return Task.CompletedTask;
    }
}
