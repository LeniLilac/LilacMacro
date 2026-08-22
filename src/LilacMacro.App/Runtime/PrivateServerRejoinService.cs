using LilacMacro.App.Debugging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Windows;
using LilacMacro.Windows.Capture;

namespace LilacMacro.App.Runtime;

internal sealed class PrivateServerRejoinService(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);
    private const int CaptureFailureLimit = 3;
    private readonly DebugOcrController _debug = new(workspace, ocr);
    private readonly RobloxClientLifecycleService _lifecycle = new();
    private readonly RobloxProtocolLauncher _launcher = new();

    public async Task RejoinAndVerifyLobbyAsync(
        string privateServerLink,
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        RobloxPrivateServerLaunchTarget target = Validate(privateServerLink);
        await _lifecycle.PrepareForPrivateServerLaunchAsync(status, cancellationToken).ConfigureAwait(false);
        await _launcher.LaunchAsync(target.LaunchUri, cancellationToken).ConfigureAwait(false);
        status?.Invoke("PRIVATE SERVER REJOIN STARTED");

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);
        try
        {
            int consecutiveCaptureFailures = 0;
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
                    consecutiveCaptureFailures = 0;
                }
                catch (RobloxCaptureUnavailableException error)
                {
                    consecutiveCaptureFailures++;
                    status?.Invoke(
                        $"ROBLOX CAPTURE RECOVERY {consecutiveCaptureFailures}/{CaptureFailureLimit}");
                    if (consecutiveCaptureFailures >= CaptureFailureLimit)
                    {
                        throw new InvalidOperationException(
                            "Windows capture remained unavailable after Roblox reopened.",
                            error);
                    }
                }
                catch (Exception error) when (error is InvalidOperationException or ArgumentException)
                {
                    consecutiveCaptureFailures = 0;
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

    internal static RobloxPrivateServerLaunchTarget Validate(string value)
    {
        try
        {
            return RobloxPrivateServerLaunchTarget.Parse(value);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidOperationException("Set a valid Roblox private-server link in Settings.", exception);
        }
    }
}
