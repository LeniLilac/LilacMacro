using System.Diagnostics;
using LilacMacro.App.Debugging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Windows;

namespace LilacMacro.App.Runtime;

internal sealed class PrivateServerRejoinService(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);
    private readonly DebugOcrController _debug = new(workspace, ocr);
    private readonly RobloxClientLifecycleService _lifecycle = new();

    public async Task RejoinAndVerifyLobbyAsync(
        string privateServerLink,
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        Uri uri = Validate(privateServerLink);
        await _lifecycle.PrepareForPrivateServerLaunchAsync(status, cancellationToken).ConfigureAwait(false);
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        status?.Invoke("PRIVATE SERVER REJOIN STARTED");

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), deadline.Token);
            while (true)
            {
                try
                {
                    await workspace.RefreshWindowAsync(deadline.Token);
                    DebugRunReport report = await _debug.CheckLobbyAsync(device, deadline.Token);
                    if (report.Succeeded)
                    {
                        status?.Invoke("LOBBY VERIFIED AFTER REJOIN");
                        return;
                    }
                }
                catch (Exception error) when (error is InvalidOperationException or ArgumentException)
                {
                    status?.Invoke("WAITING FOR ROBLOX LOBBY");
                }
                await Task.Delay(TimeSpan.FromSeconds(1), deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Lobby was not verified within two minutes of private-server rejoin.");
        }
    }

    internal static Uri Validate(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.Equals("roblox.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".roblox.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Set a valid HTTPS roblox.com private-server link in Settings.");
        }
        return uri;
    }
}
