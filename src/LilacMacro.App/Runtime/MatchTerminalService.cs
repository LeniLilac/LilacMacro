using LilacMacro.App.Debugging;
using LilacMacro.App.Infrastructure;
using LilacMacro.App.Workspace;

namespace LilacMacro.App.Runtime;

internal sealed class MatchTerminalService(
    WorkspaceController workspace,
    OcrRunner ocr)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private readonly DebugOcrStateRunner _states = new(workspace, ocr);
    private readonly DebugResultRunner _results = new(workspace, ocr);

    public async Task<MatchTerminalOutcome> WaitAsync(
        string device,
        TimeSpan timeout,
        bool dismissRaidDrops,
        Action<string>? status,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            while (true)
            {
                DebugOcrSnapshot snapshot = await _states.RunAsync(
                    DebugWorkflowCatalog.Victory, device, deadline.Token);
                if (snapshot.Evaluation.IsMatch)
                {
                    status?.Invoke("VICTORY VERIFIED");
                    return MatchTerminalOutcome.Victory;
                }
                if (DebugOcrStateRunner.Evaluate(DebugWorkflowCatalog.Defeat, snapshot.Regions).IsMatch)
                {
                    status?.Invoke("DEFEAT VERIFIED");
                    return MatchTerminalOutcome.Defeat;
                }
                if (dismissRaidDrops)
                {
                    await workspace.ClickRobloxAsync(
                        DebugWorkflowCatalog.ClientSize,
                        RaidDropDismissalPolicy.ActionPoint,
                        deadline.Token);
                    status?.Invoke("RAID DROP DISMISSAL CLICK");
                }
                status?.Invoke("WAITING FOR VICTORY / DEFEAT");
                await Task.Delay(PollInterval, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Victory or Defeat was not verified within {timeout.TotalMinutes:N0} minutes.");
        }
    }

    public async Task RepeatAsync(
        MatchTerminalOutcome outcome,
        string device,
        CancellationToken cancellationToken)
    {
        DebugStateSpec state = outcome == MatchTerminalOutcome.Victory
            ? DebugWorkflowCatalog.Victory
            : DebugWorkflowCatalog.Defeat;
        DebugRunReport report = await _results.RepeatAsync(state, device, cancellationToken);
        if (!report.Succeeded) throw new InvalidOperationException(report.Status);
    }
}
