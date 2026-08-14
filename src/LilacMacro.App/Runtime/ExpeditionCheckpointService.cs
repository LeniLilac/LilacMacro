using LilacMacro.App.Debugging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;

namespace LilacMacro.App.Runtime;

internal sealed class ExpeditionCheckpointService(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private static readonly PixelRect Controls = new(360, 170, 650, 440);
    private readonly ExpeditionOcrService _ocr = new(workspace, ocr);

    public Task ContinueAsync(string device, Action<string>? status, CancellationToken cancellationToken) =>
        RunAsync("continue", device, status, cancellationToken);

    public Task ExtractAsync(string device, Action<string>? status, CancellationToken cancellationToken) =>
        RunAsync("extract", device, status, cancellationToken);

    private async Task RunAsync(
        string action,
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OcrTextRegion> confirmation = await OpenConfirmationAsync(
            action, device, status, cancellationToken).ConfigureAwait(false);
        await ConfirmAndWaitForClosedAsync(
            action, confirmation, device, status, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<OcrTextRegion>> OpenConfirmationAsync(
        string action,
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        int actions = 0;
        for (int observation = 0; observation < 24; observation++)
        {
            IReadOnlyList<OcrTextRegion> regions = await ObserveAsync(device, cancellationToken)
                .ConfigureAwait(false);
            bool hasAction = regions.Any(region => Contains(region.Text, action));
            bool hasCancel = regions.Any(region => Contains(region.Text, "cancel"));
            if (hasAction && hasCancel) return regions;
            if (hasAction && actions < 4)
            {
                OcrTextRegion first = Select(regions, action, preferRightmost: action == "continue");
                await workspace.ClickRobloxAsync(
                    DebugWorkflowCatalog.ClientSize, first.Bounds.Center, cancellationToken).ConfigureAwait(false);
                actions++;
                status?.Invoke($"CHECKPOINT {action.ToUpperInvariant()} CLICK {actions}/4");
            }
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException($"Checkpoint {action} confirmation was not verified.");
    }

    private async Task ConfirmAndWaitForClosedAsync(
        string action,
        IReadOnlyList<OcrTextRegion> confirmation,
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        int actions = 0;
        int closedObservations = 0;
        IReadOnlyList<OcrTextRegion> current = confirmation;
        for (int observation = 0; observation < 24; observation++)
        {
            bool hasAction = current.Any(region => Contains(region.Text, action));
            bool hasCancel = current.Any(region => Contains(region.Text, "cancel"));
            if (!hasAction && !hasCancel)
            {
                closedObservations++;
                if (closedObservations >= 2)
                {
                    status?.Invoke($"CHECKPOINT {action.ToUpperInvariant()} CONFIRMED");
                    return;
                }
            }
            else
            {
                closedObservations = 0;
            }
            if (hasAction && hasCancel && actions < 4)
            {
                OcrTextRegion confirm = Select(current, action, preferRightmost: false);
                await workspace.ClickRobloxAsync(
                    DebugWorkflowCatalog.ClientSize, confirm.Bounds.Center, cancellationToken).ConfigureAwait(false);
                actions++;
                status?.Invoke($"CHECKPOINT {action.ToUpperInvariant()} CONFIRM {actions}/4");
            }
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);
            current = await ObserveAsync(device, cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException($"Checkpoint {action} confirmation did not close.");
    }

    private Task<IReadOnlyList<OcrTextRegion>> ObserveAsync(
        string device,
        CancellationToken cancellationToken) =>
        _ocr.ObserveAsync(Controls, device, cancellationToken);

    private static OcrTextRegion Select(
        IEnumerable<OcrTextRegion> regions,
        string target,
        bool preferRightmost)
    {
        OcrTextRegion[] matches = regions.Where(region => Contains(region.Text, target)).ToArray();
        if (matches.Length == 0) throw new InvalidOperationException($"Checkpoint did not expose {target}.");
        return preferRightmost
            ? matches.OrderByDescending(region => region.Bounds.Center.X).First()
            : matches.OrderByDescending(region => region.Bounds.Center.Y).ThenBy(region => region.Bounds.Center.X).First();
    }

    private static bool Contains(string value, string target) =>
        new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray())
            .Contains(target, StringComparison.Ordinal);
}
