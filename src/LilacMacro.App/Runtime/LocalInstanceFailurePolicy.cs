using System.ComponentModel;

namespace LilacMacro.App.Runtime;

internal sealed record LocalInstanceFailureObservation(
    string Operation,
    string FailureCode,
    string ConfigurationMode,
    int DurationMilliseconds,
    int? ProcessExitCode,
    int RunnerCount);

internal static class LocalInstanceFailurePolicy
{
    internal static string OperationFor(IReadOnlyList<string> arguments) =>
        arguments.FirstOrDefault() switch
        {
            "install" => "setup",
            "repair" => "repair",
            "remove" => "remove-all",
            "add-shared" => "add-shared",
            "add-isolated" => "add-isolated",
            "remove-profile" => "remove-profile",
            _ => "refresh",
        };

    internal static string ConfigurationModeFor(string operation) => operation switch
    {
        "add-shared" => "shared",
        "add-isolated" => "isolated",
        _ => "not-applicable",
    };

    internal static string Classify(
        string operation,
        string? statusCode,
        Exception? error,
        int? processExitCode,
        bool helperStarted)
    {
        if (error is OperationCanceledException) return "canceled";
        if (error is FileNotFoundException) return "helper-missing";
        if (error is UnauthorizedAccessException) return "access-denied";
        if (error is IOException) return "io-failure";
        if (error is InvalidDataException) return "invalid-state";
        if (!helperStarted && error is InvalidOperationException or Win32Exception)
            return "helper-start-failed";
        if (statusCode is not null)
        {
            return statusCode switch
            {
                "preflight-rejected" or "native-compatibility-changed" => "preflight-rejected",
                "setup-preparation-failed" or "setup-failed-rolled-back" => "setup-rolled-back",
                "setup-helper-failed" => "helper-failed",
                "cleanup-incomplete" or "rollback-incomplete" => "cleanup-incomplete",
                "instance-manager-ready" when processExitCode.HasValue && processExitCode.Value != 0
                    => "operation-incomplete",
                _ => "operation-failed",
            };
        }
        if (error is Win32Exception) return "windows-failure";
        if (processExitCode.HasValue && processExitCode.Value != 0) return "operation-incomplete";
        return operation == "setup" ? "helper-failed" : "operation-failed";
    }

    internal static int BoundedDuration(TimeSpan elapsed) =>
        (int)Math.Clamp(elapsed.TotalMilliseconds, 0, 600_000);

    internal static int? BoundedExitCode(int? exitCode) =>
        exitCode is >= 0 and <= 65_535 ? exitCode : null;

    internal static int BoundedRunnerCount(int count) => Math.Clamp(count, 0, 16);
}
