using LilacMacro.Windows;

namespace LilacMacro.App.Workspace;

public sealed partial class WorkspaceController
{
    public async Task RefreshWindowAsync(
        CancellationToken cancellationToken = default,
        bool waitForCapturable = false)
    {
        RobloxWindowAcquisitionWaiter waiter = new(_windows.AcquireBest);
        int attemptLimit = waitForCapturable ? RobloxWindowAcquisitionWaiter.MaximumAttempts : 1;
        RobloxWindowAcquisition acquisition = await waiter.RunAsync(
            waitForCapturable,
            (attempt, result) => RecordWindowAcquisition(attempt, attemptLimit, result),
            cancellationToken);

        RobloxWindow = acquisition.Window;
        ObservedClientSize = acquisition.Bounds?.Size;
        _deepDebug.RecordEvent("window", "refreshed", new
        {
            Found = RobloxWindow is not null,
            RobloxWindow?.Title,
            RobloxWindow?.ProcessId,
            ObservedClientSize,
            CandidateCount = acquisition.Candidates.Count,
        });
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RecordWindowAcquisition(
        int attempt,
        int attemptLimit,
        RobloxWindowAcquisition acquisition) =>
        _deepDebug.RecordEvent("window", "acquisition_observed", new
        {
            Attempt = attempt,
            AttemptLimit = attemptLimit,
            Succeeded = acquisition.Succeeded,
            CandidateCount = acquisition.Candidates.Count,
            Candidates = acquisition.Candidates.Select(candidate => new
            {
                candidate.Window.ProcessId,
                candidate.Window.ProcessName,
                candidate.ClientSize,
                candidate.InitialClientWidth,
                candidate.InitialClientHeight,
                candidate.WasMinimized,
                candidate.Outcome,
            }),
        });
}
