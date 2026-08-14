using LilacMacro.App.Debugging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;

namespace LilacMacro.App.Runtime;

internal sealed class ExpeditionEncounterService(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private static readonly PixelRect Interaction = new(320, 350, 720, 310);
    private readonly ExpeditionOcrService _ocr = new(workspace, ocr);
    private readonly ExpeditionSettingsService _settings = new(workspace, ocr);

    public async Task<bool> RunAsync(
        string map,
        int reservedVirtualKey,
        string device,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        status?.Invoke("ENCOUNTER TRAVEL WAIT 15S");
        await Task.Delay(ExpeditionEncounterPolicy.TravelDelayMilliseconds, cancellationToken).ConfigureAwait(false);
        await _settings.TeleportToSpawnAsync(device, status, cancellationToken).ConfigureAwait(false);

        ExpeditionEncounterMovement movement = ExpeditionEncounterPolicy.ForMap(map);
        await PressAsync('W', movement.ForwardMilliseconds, reservedVirtualKey, cancellationToken).ConfigureAwait(false);
        await PressAsync('D', movement.RightMilliseconds, reservedVirtualKey, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<OcrTextRegion>? menu = null;
        for (int attempt = 1; attempt <= ExpeditionEncounterPolicy.MaximumInteractionAttempts; attempt++)
        {
            await PressAsync('E', 80, reservedVirtualKey, cancellationToken).ConfigureAwait(false);
            menu = await WaitForMenuAsync(device, cancellationToken).ConfigureAwait(false);
            if (menu is not null) break;
            status?.Invoke($"ENCOUNTER INTERACTION MISS {attempt}/{ExpeditionEncounterPolicy.MaximumInteractionAttempts}");
        }
        if (menu is null)
        {
            status?.Invoke("ENCOUNTER INTERACTION FAILED | VERIFIED RESTART");
            await _settings.RestartAsync(device, status, cancellationToken).ConfigureAwait(false);
            return false;
        }

        OcrTextRegion left = menu
            .Where(region => Is(region.Text, "speak") || Is(region.Text, "discuss"))
            .OrderBy(region => region.Bounds.Center.X)
            .First();
        PixelPoint leftAction = left.Bounds.Center;
        PixelPoint dialogue = new(683, 500);
        for (int click = 0; click < 20; click++)
        {
            await workspace.ClickRobloxAsync(
                DebugWorkflowCatalog.ClientSize,
                click % 3 == 0 ? leftAction : dialogue,
                cancellationToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken).ConfigureAwait(false);
        }
        status?.Invoke("ENCOUNTER DIALOGUE ADVANCED");
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<IReadOnlyList<OcrTextRegion>?> WaitForMenuAsync(
        string device,
        CancellationToken cancellationToken)
    {
        for (int observation = 0; observation < 6; observation++)
        {
            IReadOnlyList<OcrTextRegion> regions = await _ocr.ObserveAsync(
                Interaction, device, cancellationToken).ConfigureAwait(false);
            bool first = regions.Any(region => Is(region.Text, "speak") || Is(region.Text, "discuss"));
            int support = new[] { "barter", "engage", "leave" }
                .Count(target => regions.Any(region => Is(region.Text, target)));
            if (first && support >= 2) return regions;
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
        return null;
    }

    private Task PressAsync(
        int virtualKey,
        int holdMilliseconds,
        int reservedVirtualKey,
        CancellationToken cancellationToken) => workspace.RunKeySequenceAsync(
        DebugWorkflowCatalog.ClientSize,
        AutomationKeySequence.Create(
        [
            AutomationKeyPress.Create(virtualKey, holdMilliseconds, reservedVirtualKey),
        ]),
        cancellationToken);

    private static bool Is(string value, string target) =>
        new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray())
            .Equals(target, StringComparison.Ordinal);
}
