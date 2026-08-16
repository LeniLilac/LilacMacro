using LilacMacro.Windows;
using LilacMacro.Windows.Capture;

namespace LilacMacro.App.Workspace;

public sealed partial class WorkspaceController
{
    private const int MaximumWebhookScreenshotBytes = 8 * 1024 * 1024;

    public async Task<byte[]?> CaptureWebhookScreenshotAsync(CancellationToken cancellationToken = default)
    {
        if (!await _operationGate.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false))
            return null;
        try
        {
            RobloxWindow? window = RobloxWindow ?? _windows.FindBest();
            if (window is null) return null;
            RobloxWindow selectedWindow = window.Value;

            CapturedPng image = await Task.Run(
                () => _capture.Capture(selectedWindow),
                cancellationToken).ConfigureAwait(false);
            return image.Bytes.Length is > 0 and <= MaximumWebhookScreenshotBytes
                ? image.Bytes.ToArray()
                : null;
        }
        finally
        {
            _operationGate.Release();
        }
    }
}
