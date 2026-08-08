using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;
using static LilacMacro.App.Debugging.DebugReportFactory;

namespace LilacMacro.App.Debugging;

internal sealed class DebugUnitInventoryRunner(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);

    public async Task<DebugRunReport> CheckAsync(
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot snapshot = await RunStateAsync(device, cancellationToken);
        return StateReport(snapshot);
    }

    public async Task<DebugRunReport> OpenTeamsAsync(
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot snapshot = await RunStateAsync(device, cancellationToken);
        if (!snapshot.Evaluation.IsMatch) return FailedState(snapshot);

        OcrTargetMatch? teams = snapshot.Evaluation.Matches.FirstOrDefault(
            match => match.Target.Equals("Teams", StringComparison.Ordinal));
        if (teams is null) return MissingTarget(snapshot, "TEAMS");
        PixelPoint point = teams.Region.Bounds.Center;
        await workspace.ClickRobloxAsync(
            DebugWorkflowCatalog.ClientSize,
            point,
            cancellationToken);
        return ClickReport(snapshot, teams, point, "CENTER");
    }

    private Task<DebugOcrSnapshot> RunStateAsync(
        string device,
        CancellationToken cancellationToken) => _states.RunAsync(
        DebugWorkflowCatalog.UnitInventory,
        device,
        cancellationToken);
}
