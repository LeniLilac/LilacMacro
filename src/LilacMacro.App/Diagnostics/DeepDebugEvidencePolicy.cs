using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LilacMacro.App.Diagnostics;

internal enum DeepDebugErrorSeverity
{
    Recoverable = 1,
    Terminal = 2,
}

internal sealed record DeepDebugErrorMarker(
    DateTimeOffset TimestampUtc,
    DeepDebugErrorSeverity Severity,
    string Signature);

internal static partial class DeepDebugEvidencePolicy
{
    public static readonly TimeSpan ErrorWindowBefore = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan ErrorWindowAfter = TimeSpan.FromSeconds(10);

    public static bool TryClassifyError(
        string category,
        string action,
        object? data,
        DateTimeOffset timestampUtc,
        out DeepDebugErrorMarker? marker)
    {
        DeepDebugErrorSeverity? severity = (category, action) switch
        {
            ("application", "unhandled_exception") => DeepDebugErrorSeverity.Terminal,
            ("macro", "runtime_error") => DeepDebugErrorSeverity.Terminal,
            ("macro", "runtime_recovery") => DeepDebugErrorSeverity.Recoverable,
            ("ocr_setup", "setup_failed") => DeepDebugErrorSeverity.Recoverable,
            ("ocr", "inference_failed") => DeepDebugErrorSeverity.Recoverable,
            ("ocr", "worker_timeout") => DeepDebugErrorSeverity.Recoverable,
            ("local_instance", "operation_failed") => DeepDebugErrorSeverity.Recoverable,
            ("vision", "profile_refresh_failed") => DeepDebugErrorSeverity.Recoverable,
            ("route_optimizer_test", "trial_failed") => DeepDebugErrorSeverity.Recoverable,
            ("team_swap_test", "trial_error") => DeepDebugErrorSeverity.Recoverable,
            ("session", "finished") when string.Equals(
                ReadText(data, "Outcome"), "error", StringComparison.OrdinalIgnoreCase) =>
                DeepDebugErrorSeverity.Terminal,
            _ when IsBoundedFailure(category, action) => DeepDebugErrorSeverity.Recoverable,
            _ => null,
        };
        if (severity is null)
        {
            marker = null;
            return false;
        }

        string details = string.Join('|', new[]
        {
            ReadText(data, "FailureCode"),
            ReadText(data, "Stage"),
            ReadText(data, "Operation"),
            ReadText(data, "FailedTask"),
            NormalizeError(ReadText(data, "Error")),
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        string identity = $"{category}/{action}|{details}";
        string signature = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
        marker = new(timestampUtc, severity.Value, signature);
        return true;
    }

    public static string? TransitionSignature(string category, string action)
    {
        if (category is not (
                "session" or "workspace" or "window" or "wire" or "game_settings" or
                "ui_scale" or "challenge" or "tower" or "macro")) return null;
        if (action is "started" or "finished" or "initialize_completed" or "client_resized" ||
            action.EndsWith("_completed", StringComparison.Ordinal) ||
            action.EndsWith("_verified", StringComparison.Ordinal) ||
            action.EndsWith("_selected", StringComparison.Ordinal) ||
            action is "terminal_continuation_decided")
        {
            return $"{category}/{action}";
        }
        return null;
    }

    private static bool IsBoundedFailure(string category, string action) =>
        !(category == "diagnostic" && action == "periodic_live_frame_capture_failed") &&
        category is "wire" or "tower" or "challenge" or "configuration" or "debug" or
            "diagnostic" or "input" or "window" or "ocr" or "ocr_setup" or "local_instance" or
            "route_optimizer_test" or "team_swap_test" or "vision"
        && (action.EndsWith("_failed", StringComparison.Ordinal) ||
            action.EndsWith("_exhausted", StringComparison.Ordinal));

    private static string ReadText(object? data, string name)
    {
        if (data is null) return string.Empty;
        PropertyInfo? property = data.GetType().GetProperties()
            .FirstOrDefault(candidate => string.Equals(
                candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        return property?.GetValue(data)?.ToString() ?? string.Empty;
    }

    private static string NormalizeError(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string firstLine = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? value;
        string normalized = DigitsRegex().Replace(
            DeepDebugRedactor.Redact(firstLine).Trim(), "#");
        return normalized[..Math.Min(normalized.Length, 160)];
    }

    [GeneratedRegex("[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex DigitsRegex();
}
