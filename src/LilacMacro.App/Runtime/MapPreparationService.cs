using LilacMacro.App.Debugging;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Runtime;

internal sealed class MapPreparationService(WorkspaceController workspace)
{
    public async Task PrepareAsync(
        string mapId,
        int reservedVirtualKey,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MapPreparationStep> steps = MapPreparationPolicy.For(mapId);
        if (steps.Count == 0) return;

        AutomationKeyPress[] presses = steps
            .Select(step => AutomationKeyPress.Create(
                step.VirtualKey,
                step.HoldMilliseconds,
                reservedVirtualKey))
            .ToArray();
        await workspace.RunKeySequenceAsync(
            DebugWorkflowCatalog.ClientSize,
            AutomationKeySequence.Create(presses),
            cancellationToken);
        status?.Invoke($"MAP PREPARED | {string.Join(" + ", presses.Select(press => $"{press.KeyName.ToUpperInvariant()} {press.HoldMilliseconds} MS"))}");
    }
}
