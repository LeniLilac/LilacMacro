namespace LilacMacro.App.Diagnostics;

public sealed partial class DeepDebugSessionService
{
    internal async Task CompleteAsync(DeepDebugSession session, string outcome, Exception? error)
    {
        if (!session.Completion.TryOwn())
        {
            Exception? completionError = await session.Completion.WaitAsync();
            if (completionError is not null) throw completionError;
            return;
        }
        try
        {
            await CompleteOwnedAsync(session, outcome, error);
            session.Completion.Finish(null);
        }
        catch (Exception finalizationError)
        {
            session.Completion.Finish(finalizationError);
            throw;
        }
    }

    private async Task CompleteOwnedAsync(DeepDebugSession session, string outcome, Exception? error)
    {
        if (!ReferenceEquals(ActiveSession(), session)) return;
        if (session.FrameCaptureLoop is { } frameCaptureLoop)
        {
            await frameCaptureLoop.StopAsync();
            session.FrameCaptureLoop = null;
        }
        RecordEvent("session", "finished", new
        {
            Outcome = outcome,
            Error = error is null ? null : DeepDebugRedactor.Redact(error.ToString()),
        });
        lock (_gate)
        {
            if (ReferenceEquals(_active, session)) _active = null;
        }
        session.Channel.Writer.TryComplete();
        try
        {
            await session.WriterTask;
        }
        catch (Exception writerError)
        {
            session.WriterFailure ??= writerError;
        }

        DateTimeOffset completedAtUtc = _utcNow();
        if (session.FrameOptimizer is { } optimizer)
        {
            session.OptimizationMetrics = await optimizer.CompleteAsync(completedAtUtc);
            optimizer.Dispose();
            session.FrameOptimizer = null;
        }
        string archive = await Task.Run(() => _archiveFinalizer.FinalizeAsync(
            session,
            outcome,
            error,
            completedAtUtc));
        LastArchivePath = archive;
        TryDeleteDirectory(session.StagingDirectory);
        _configurationStore.PruneArchives(Options);
        NotifyArchiveSaved(ArchiveSaved, archive);
        if (session.Evidence.WindowCount > 0)
            NotifyArchiveSaved(AutomaticReportArchiveSaved, archive);
    }
}
