using System.Diagnostics;
using System.Security.Cryptography;
using LilacMacro.App.Diagnostics;

namespace LilacMacro.App.Debugging;

internal sealed record TeamSwapTrialResult(
    int Trial,
    int Team,
    bool Succeeded,
    long ElapsedMilliseconds,
    string Status);

internal sealed record TeamSwapTestProgress(
    int Completed,
    int Total,
    TeamSwapTrialResult? Trial,
    string Detail);

internal sealed record TeamSwapTestResult(
    int Seed,
    IReadOnlyList<TeamSwapTrialResult> Trials)
{
    public int Passed => Trials.Count(trial => trial.Succeeded);
    public int Failed => Trials.Count - Passed;
}

internal sealed class TeamSwapTestRunner(
    DebugOcrController debug,
    DeepDebugSessionService deepDebug)
{
    private const int InterTrialSettleMilliseconds = 150;

    public async Task<TeamSwapTestResult> RunAsync(
        int trialCount,
        string device,
        IProgress<TeamSwapTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        int seed = RandomNumberGenerator.GetInt32(int.MaxValue);
        IReadOnlyList<int> teams = CreateBalancedRandomTeams(trialCount, seed);
        List<TeamSwapTrialResult> results = [];
        deepDebug.RecordEvent("team_swap_test", "schedule", new { Seed = seed, Teams = teams });

        for (int index = 0; index < teams.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int trial = index + 1;
            int team = teams[index];
            progress?.Report(new TeamSwapTestProgress(
                trial - 1, trialCount, null, $"TRIAL {trial}/{trialCount} · TEAM {team}"));
            deepDebug.RecordEvent("team_swap_test", "trial_started", new { Trial = trial, Team = team });

            Stopwatch stopwatch = Stopwatch.StartNew();
            TeamSwapTrialResult result;
            try
            {
                DebugRunReport report = await debug.LoadTeamAsync(team, device, cancellationToken);
                stopwatch.Stop();
                result = new TeamSwapTrialResult(
                    trial, team, report.Succeeded, stopwatch.ElapsedMilliseconds, report.Status);
                deepDebug.RecordEvent("team_swap_test", "trial_completed", new
                {
                    result.Trial,
                    result.Team,
                    result.Succeeded,
                    result.ElapsedMilliseconds,
                    result.Status,
                    report.Events,
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                stopwatch.Stop();
                result = new TeamSwapTrialResult(
                    trial, team, false, stopwatch.ElapsedMilliseconds, error.Message);
                deepDebug.RecordEvent("team_swap_test", "trial_error", new
                {
                    result.Trial,
                    result.Team,
                    result.ElapsedMilliseconds,
                    Error = error.ToString(),
                });
            }

            results.Add(result);
            progress?.Report(new TeamSwapTestProgress(
                trial, trialCount, result,
                $"TRIAL {trial}/{trialCount} · TEAM {team} · {(result.Succeeded ? "PASS" : "FAIL")}"));
            if (trial < trialCount)
                await Task.Delay(InterTrialSettleMilliseconds, cancellationToken);
        }

        TeamSwapTestResult summary = new(seed, results);
        deepDebug.RecordEvent("team_swap_test", "summary", new
        {
            summary.Seed,
            summary.Passed,
            summary.Failed,
            Trials = summary.Trials.Count,
        });
        return summary;
    }

    internal static IReadOnlyList<int> CreateBalancedRandomTeams(int count, int seed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        Random random = new(seed);
        List<int> result = new(count);
        while (result.Count < count)
        {
            int[] block = [1, 2, 3, 4, 5, 6, 7, 8];
            for (int index = block.Length - 1; index > 0; index--)
            {
                int swap = random.Next(index + 1);
                (block[index], block[swap]) = (block[swap], block[index]);
            }
            int take = Math.Min(block.Length, count - result.Count);
            result.AddRange(block.Take(take));
        }
        return result;
    }
}
