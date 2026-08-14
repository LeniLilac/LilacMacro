using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;

namespace LilacMacro.App.Runtime;

internal sealed class ExpeditionEncounterService(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private readonly ExpeditionCheckpointService _checkpoint = new(workspace, ocr);

    public Task RunAsync(
        string device,
        Action<string>? status,
        CancellationToken cancellationToken) =>
        _checkpoint.ContinueEncounterAsync(device, status, cancellationToken);
}
