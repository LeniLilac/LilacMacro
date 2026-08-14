using System.Diagnostics;
using LilacMacro.App.Diagnostics;
using LilacMacro.App.Runtime;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Debugging;

internal sealed record RouteOptimizerTrialResult(
    int Trial,
    int? Quantity,
    int? Threshold,
    string Decision,
    long? RerollMilliseconds,
    IReadOnlyList<string> OcrText,
    string? Error = null);

internal sealed record RouteOptimizerTestProgress(
    int Completed,
    int Total,
    RouteOptimizerTrialResult? Trial,
    string Detail);

internal sealed record RouteOptimizerTestResult(
    IReadOnlyList<RouteOptimizerTrialResult> Trials)
{
    public int Accepted => Trials.Count(trial => trial.Decision == "ACCEPT");
    public int Rerolled => Trials.Count(trial => trial.Decision == "REROLL");
    public int Errors => Trials.Count(trial => trial.Decision == "ERROR");
}

internal sealed class RouteOptimizerTestRunner(
    ExpeditionRewardPoolService rewards,
    ExpeditionSettingsService settings,
    DeepDebugSessionService deepDebug,
    ExpeditionRewardProfileStore profiles)
{
    public async Task<RouteOptimizerTestResult> RunAsync(
        int trialCount,
        int difficulty,
        ExpeditionRewardResource target,
        string device,
        IProgress<RouteOptimizerTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        trialCount = ExpeditionRewardPolicy.ValidateTestTrials(trialCount);
        if (target == ExpeditionRewardResource.None)
            throw new InvalidDataException("A reward target is required.");
        List<RouteOptimizerTrialResult> trials = [];
        bool routeOpen = false;
        Stopwatch? reroll = null;

        for (int index = 0; index < trialCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int trial = index + 1;
            progress?.Report(new RouteOptimizerTestProgress(
                trial - 1, trialCount, null, $"TRIAL {trial}/{trialCount} | READING"));
            if (!routeOpen)
                await rewards.OpenAsync(device, cancellationToken).ConfigureAwait(false);
            routeOpen = false;
            ExpeditionRewardObservation? observation = null;
            RouteOptimizerTrialResult result;
            try
            {
                observation = await rewards.ObserveAsync(target, device, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                reroll?.Stop();
                result = new RouteOptimizerTrialResult(
                    trial, null, null, "ERROR", reroll?.ElapsedMilliseconds, [], error.Message);
                trials.Add(result);
                deepDebug.RecordEvent("route_optimizer_test", "trial_failed", new
                {
                    result.Trial,
                    Difficulty = difficulty,
                    Target = target.ToString(),
                    result.RerollMilliseconds,
                    result.Error,
                });
                progress?.Report(new RouteOptimizerTestProgress(trial, trialCount, result,
                    $"TRIAL {trial}/{trialCount} | ERROR"));
                await rewards.BackToPrestartAfterReadFailureAsync(device, cancellationToken).ConfigureAwait(false);
                if (trial == trialCount) continue;
                reroll = Stopwatch.StartNew();
                await PrepareNextTrialAsync(trial, trialCount, device, progress, cancellationToken)
                    .ConfigureAwait(false);
                routeOpen = true;
                continue;
            }

            if (reroll is not null)
            {
                reroll.Stop();
                await profiles.RecordRerollAsync(device, reroll.Elapsed, cancellationToken).ConfigureAwait(false);
            }
            await profiles.RecordPoolAsync(difficulty, observation.Pool, cancellationToken).ConfigureAwait(false);
            ExpeditionRewardOptimization? optimization = await profiles.OptimizeAsync(
                difficulty, target, device, cancellationToken).ConfigureAwait(false);
            int quantity = observation.Pool.Quantity(target);
            string decision = optimization is null
                ? "COLLECT"
                : quantity >= optimization.Threshold ? "ACCEPT" : "REROLL";
            result = new RouteOptimizerTrialResult(
                trial,
                quantity,
                optimization?.Threshold,
                decision,
                reroll?.ElapsedMilliseconds,
                observation.OcrText);
            trials.Add(result);
            deepDebug.RecordEvent("route_optimizer_test", "trial_observed", new
            {
                result.Trial,
                Difficulty = difficulty,
                Target = target.ToString(),
                result.Quantity,
                result.Threshold,
                result.Decision,
                result.RerollMilliseconds,
                result.OcrText,
            });
            progress?.Report(new RouteOptimizerTestProgress(
                trial, trialCount, result,
                $"TRIAL {trial}/{trialCount} | {decision}"));

            await rewards.BackToPrestartAsync(observation, device, cancellationToken).ConfigureAwait(false);
            if (trial == trialCount) continue;

            reroll = Stopwatch.StartNew();
            await PrepareNextTrialAsync(trial, trialCount, device, progress, cancellationToken)
                .ConfigureAwait(false);
            routeOpen = true;
        }

        RouteOptimizerTestResult summary = new(trials);
        deepDebug.RecordEvent("route_optimizer_test", "summary", new
        {
            Target = target.ToString(),
            Difficulty = difficulty,
            Trials = summary.Trials.Count,
            summary.Accepted,
            summary.Rerolled,
            summary.Errors,
        });
        return summary;
    }

    private async Task PrepareNextTrialAsync(
        int trial,
        int trialCount,
        string device,
        IProgress<RouteOptimizerTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        await rewards.StartGameForRouteAsync(device, cancellationToken).ConfigureAwait(false);
        await settings.RestartForRouteRerollAsync(
            device,
            detail => progress?.Report(new RouteOptimizerTestProgress(
                trial, trialCount, null, detail)),
            cancellationToken).ConfigureAwait(false);
        progress?.Report(new RouteOptimizerTestProgress(
            trial, trialCount, null, $"TRIAL {trial + 1}/{trialCount} | OPENING ROUTE"));
        await rewards.OpenAfterRestartAsync(device, cancellationToken).ConfigureAwait(false);
    }

}
