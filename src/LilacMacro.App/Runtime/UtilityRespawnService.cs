using LilacMacro.App.Debugging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Geometry;

namespace LilacMacro.App.Runtime;

internal sealed class UtilityRespawnService(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private static readonly PixelSize ClientSize = DebugWorkflowCatalog.ClientSize;
    private static readonly TimeSpan ObservationDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan KeyDelay = TimeSpan.FromMilliseconds(250);
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);

    public async Task RunAsync(
        int? areasMenuVirtualKey,
        int reservedVirtualKey,
        string device,
        CancellationToken cancellationToken)
    {
        if (areasMenuVirtualKey is null)
            throw new InvalidDataException("Areas menu must have a key for Utility task cleanup.");

        IReadOnlyList<int> keyOrder = UtilityRespawnPolicy.CreateKeyOrder(areasMenuVirtualKey.Value);
        await PressAsync(keyOrder[0], reservedVirtualKey, cancellationToken).ConfigureAwait(false);
        DebugOcrSnapshot areas = await _states.WaitForMatchAsync(
            DebugWorkflowCatalog.AreasUi,
            device,
            8,
            ObservationDelay,
            cancellationToken).ConfigureAwait(false);
        if (!areas.Evaluation.IsMatch)
            throw new InvalidOperationException("Areas did not open before Utility task respawn.");

        for (int index = 1; index < keyOrder.Count; index++)
        {
            await PressAsync(keyOrder[index], reservedVirtualKey, cancellationToken).ConfigureAwait(false);
            if (index < keyOrder.Count - 1)
                await Task.Delay(KeyDelay, cancellationToken).ConfigureAwait(false);
        }

        DebugOcrSnapshot lobby = await _states.WaitForMatchAsync(
            DebugWorkflowCatalog.Lobby,
            device,
            24,
            ObservationDelay,
            cancellationToken).ConfigureAwait(false);
        if (!lobby.Evaluation.IsMatch)
            throw new InvalidOperationException("Lobby was not verified after Utility task respawn.");
    }

    internal static AutomationKeySequence CreateKeySequence(
        int virtualKey,
        int reservedVirtualKey) => AutomationKeySequence.Create(
        [AutomationKeyPress.Create(virtualKey, 80, reservedVirtualKey)]);

    private Task PressAsync(
        int virtualKey,
        int reservedVirtualKey,
        CancellationToken cancellationToken) => workspace.RunKeySequenceAsync(
        ClientSize,
        CreateKeySequence(virtualKey, reservedVirtualKey),
        cancellationToken);
}
