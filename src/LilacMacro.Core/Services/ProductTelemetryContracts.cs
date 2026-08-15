using System.Text.RegularExpressions;

namespace LilacMacro.Core.Services;

public enum ProductTelemetryKind
{
    SessionStarted,
    FeatureUsed,
    OperationError,
    ExpeditionRewardObserved,
    OcrTiming,
}

public sealed record ProductTelemetryEvent(
    ProductTelemetryKind Kind,
    DateTimeOffset OccurredAtUtc,
    string? Feature = null,
    string? Outcome = null,
    int? DurationMilliseconds = null,
    string? Material = null,
    int? Quantity = null,
    string? OperatingSystem = null,
    int? LogicalProcessorCount = null,
    string? GraphicsCapability = null);

public sealed record ProductTelemetryBatch(
    Guid InstallId,
    string AppVersion,
    int PrivacyNoticeVersion,
    IReadOnlyList<ProductTelemetryEvent> Events);

public interface IProductTelemetryTransport
{
    Task SendAsync(ProductTelemetryBatch batch, CancellationToken cancellationToken = default);
}

public static partial class ProductTelemetryPolicy
{
    public const int MaximumEventsPerBatch = 64;
    public const int MaximumRequestBytes = 64 * 1024;
    public static readonly Uri Endpoint = new("https://macro.expeditions.gg/v1/telemetry/events");

    public static void Validate(ProductTelemetryBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.InstallId == Guid.Empty) throw new InvalidDataException("Telemetry installation ID was empty.");
        if (batch.PrivacyNoticeVersion != 1) throw new InvalidDataException("Telemetry notice version was invalid.");
        if (batch.Events.Count is < 1 or > MaximumEventsPerBatch)
            throw new InvalidDataException("Telemetry event count was outside its bound.");
        if (!SemanticVersionPattern().IsMatch(batch.AppVersion))
            throw new InvalidDataException("Telemetry app version was invalid.");
        foreach (ProductTelemetryEvent item in batch.Events)
        {
            if (!Enum.IsDefined(item.Kind)) throw new InvalidDataException("Telemetry event kind was invalid.");
            ValidateEvent(item);
        }
    }

    public static string KindValue(ProductTelemetryKind kind) => kind switch
    {
        ProductTelemetryKind.SessionStarted => "session-started",
        ProductTelemetryKind.FeatureUsed => "feature-used",
        ProductTelemetryKind.OperationError => "operation-error",
        ProductTelemetryKind.ExpeditionRewardObserved => "expedition-reward-observed",
        ProductTelemetryKind.OcrTiming => "ocr-timing",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static void ValidateEvent(ProductTelemetryEvent item)
    {
        bool valid = item.Kind switch
        {
            ProductTelemetryKind.SessionStarted =>
                item.Feature == "macro" && item.Outcome == "started"
                && item.OperatingSystem is not null && OperatingSystemPattern().IsMatch(item.OperatingSystem)
                && item.LogicalProcessorCount is >= 1 and <= 512
                && item.GraphicsCapability == "not-observed"
                && item.DurationMilliseconds is null && item.Material is null && item.Quantity is null,
            ProductTelemetryKind.FeatureUsed =>
                item.Feature is "workspace" or "wire" or "challenge" or "game_settings" or "ui_scale"
                && item.Outcome == "completed" && HasNoMetrics(item),
            ProductTelemetryKind.OperationError =>
                item.Feature is "macro" or "application"
                && item.Outcome is "runtime_error" or "unhandled_exception" && HasNoMetrics(item),
            ProductTelemetryKind.ExpeditionRewardObserved =>
                item.Feature == "route-optimizer" && item.Outcome == "observed"
                && item.Material is "FuelCell" or "EquipmentScrap" or "EquipmentReroll"
                    or "EquipmentLock" or "ExpeditionCoin"
                && item.Quantity is >= 0 and <= 1_000
                && item.DurationMilliseconds is null && item.OperatingSystem is null
                && item.LogicalProcessorCount is null && item.GraphicsCapability is null,
            ProductTelemetryKind.OcrTiming =>
                item.Feature == "ocr" && item.Outcome == "completed"
                && item.DurationMilliseconds is >= 0 and <= 600_000
                && item.GraphicsCapability is "cpu" or "gpu" or "gpu:0" or "not-observed"
                && item.Material is null && item.Quantity is null && item.OperatingSystem is null
                && item.LogicalProcessorCount is null,
            _ => false,
        };
        if (!valid) throw new InvalidDataException("Telemetry event fields were invalid for its kind.");
    }

    private static bool HasNoMetrics(ProductTelemetryEvent item) =>
        item.DurationMilliseconds is null && item.Material is null && item.Quantity is null
        && item.OperatingSystem is null && item.LogicalProcessorCount is null
        && item.GraphicsCapability is null;

    [GeneratedRegex("^\\d+\\.\\d+\\.\\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionPattern();

    [GeneratedRegex("^windows-\\d{1,2}\\.\\d{1,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex OperatingSystemPattern();
}
