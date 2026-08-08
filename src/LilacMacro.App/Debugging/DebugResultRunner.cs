using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;
using static LilacMacro.App.Debugging.DebugReportFactory;

namespace LilacMacro.App.Debugging;

internal sealed class DebugResultRunner(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);

    public async Task<DebugRunReport> CheckAsync(
        DebugStateSpec state,
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot snapshot = await _states.RunAsync(state, device, cancellationToken);
        return StateReport(snapshot);
    }

    public async Task<DebugRunReport> RepeatAsync(
        DebugStateSpec state,
        string device,
        CancellationToken cancellationToken)
    {
        DebugOcrSnapshot snapshot = await _states.RunAsync(state, device, cancellationToken);
        if (!snapshot.Evaluation.IsMatch) return FailedState(snapshot);

        OcrTargetMatch? repeat = snapshot.Evaluation.Matches.FirstOrDefault(
            match => match.Target.Equals("Repeat Stage", StringComparison.Ordinal));
        if (repeat is null) return MissingTarget(snapshot, "REPEAT STAGE");
        PixelPoint point = repeat.Region.Bounds.Center;
        await workspace.ClickRobloxAsync(
            DebugWorkflowCatalog.ClientSize,
            point,
            cancellationToken);
        return ClickReport(snapshot, repeat, point, "CENTER");
    }
}
