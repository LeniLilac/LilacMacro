using System.Text.RegularExpressions;

namespace LilacMacro.Core.Services;

public enum ProductTelemetryKind
{
    SessionStarted,
    FeatureUsed,
    OperationError,
    ExpeditionRewardObserved,
    OcrTiming,
    OcrSetupFailure,
    LocalInstanceFailure,
    UiScaleCalibration,
}

public sealed record ProductTelemetryDeviceContext(
    string ProcessorModel,
    string GraphicsModel,
    int DisplayWidth,
    int DisplayHeight)
{
    public static ProductTelemetryDeviceContext Unknown { get; } = new("unknown", "unknown", 0, 0);
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
    string? GraphicsCapability = null,
    string? SetupStage = null,
    string? RequestedDevice = null,
    int? ProcessExitCode = null,
    bool? PythonLauncherPresent = null,
    bool? WingetPresent = null,
    bool? ExistingOcrPythonPresent = null,
    bool? RuntimeMarkerPresent = null,
    string? Operation = null,
    string? FailureCode = null,
    string? ConfigurationMode = null,
    int? RunnerCount = null,
    string? HardwareModel = null,
    int? DisplayWidth = null,
    int? DisplayHeight = null,
    int? InputScaleMilli = null,
    int? RenderedScaleMilli = null);

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
    public const int CurrentPrivacyNoticeVersion = 5;
    public static readonly Uri Endpoint = new("https://macro.expeditions.gg/v1/telemetry/events");

    public static void Validate(ProductTelemetryBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.InstallId == Guid.Empty) throw new InvalidDataException("Telemetry installation ID was empty.");
        if (batch.PrivacyNoticeVersion is < 1 or > CurrentPrivacyNoticeVersion)
            throw new InvalidDataException("Telemetry notice version was invalid.");
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
        ProductTelemetryKind.OcrSetupFailure => "ocr-setup-failure",
        ProductTelemetryKind.LocalInstanceFailure => "local-instance-failure",
        ProductTelemetryKind.UiScaleCalibration => "ui-scale-calibration",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool IsOcrSetupFailureCode(string? code) => code is
        "python312_missing" or
        "winget_unavailable" or
        "python_install_failed" or
        "python312_not_found" or
        "gpu_detection_failed" or
        "gpu_runtime_invalid" or
        "venv_create_failed" or
        "pip_update_failed" or
        "paddle_install_failed" or
        "paddleocr_install_failed" or
        "ocr_import_failed" or
        "runtime_not_ready" or
        "setup_process_start_failed" or
        "setup_process_failed" or
        "setup_failed";

    public static bool IsOcrSetupStage(string? stage) => stage is
        "python-bootstrap" or
        "gpu-runtime" or
        "environment" or
        "paddle" or
        "paddleocr" or
        "import-check" or
        "runtime" or
        "process" or
        "setup";

    public static bool IsLocalInstanceOperation(string? operation) => operation is
        "setup" or
        "repair" or
        "remove-all" or
        "add-shared" or
        "add-isolated" or
        "remove-profile" or
        "open" or
        "refresh";

    public static bool IsLocalInstanceFailureCode(string? code) => code is
        "preflight-rejected" or
        "setup-rolled-back" or
        "helper-failed" or
        "cleanup-incomplete" or
        "operation-incomplete" or
        "helper-missing" or
        "helper-start-failed" or
        "access-denied" or
        "io-failure" or
        "invalid-state" or
        "windows-failure" or
        "canceled" or
        "operation-failed";

    private static void ValidateEvent(ProductTelemetryEvent item)
    {
        bool valid = item.Kind switch
        {
            ProductTelemetryKind.SessionStarted =>
                item.Feature == "macro" && item.Outcome == "started"
                && item.OperatingSystem is not null && OperatingSystemPattern().IsMatch(item.OperatingSystem)
                && item.LogicalProcessorCount is >= 1 and <= 512
                && item.GraphicsCapability == "not-observed"
                && item.DurationMilliseconds is null && item.Material is null && item.Quantity is null
                && HasNoExtendedDetails(item),
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
                && item.LogicalProcessorCount is null && item.GraphicsCapability is null
                && HasNoExtendedDetails(item),
            ProductTelemetryKind.OcrTiming =>
                item.Feature == "ocr" && item.Outcome == "completed"
                && item.DurationMilliseconds is >= 0 and <= 600_000
                && item.GraphicsCapability is "cpu" or "gpu" or "gpu:0" or "not-observed"
                && item.Material is null && item.Quantity is null && item.OperatingSystem is null
                && item.LogicalProcessorCount is null
                && (item.GraphicsCapability == "not-observed"
                    ? item.HardwareModel is null
                    : item.HardwareModel is not null && IsHardwareModel(item.HardwareModel))
                && HasNoSetupDetails(item) && HasNoCalibrationDetails(item),
            ProductTelemetryKind.OcrSetupFailure =>
                item.Feature == "ocr-setup"
                && IsOcrSetupFailureCode(item.Outcome)
                && item.OperatingSystem is not null && OperatingSystemPattern().IsMatch(item.OperatingSystem)
                && item.SetupStage is not null && IsOcrSetupStage(item.SetupStage)
                && item.RequestedDevice is "cpu" or "gpu:0"
                && item.DurationMilliseconds is >= 0 and <= 600_000
                && item.ProcessExitCode is null or >= 0 and <= 65_535
                && item.PythonLauncherPresent is not null
                && item.WingetPresent is not null
                && item.ExistingOcrPythonPresent is not null
                && item.RuntimeMarkerPresent is not null
                && item.LogicalProcessorCount is null && item.GraphicsCapability is null
                && item.Material is null && item.Quantity is null
                && HasNoLocalInstanceDetails(item) && HasNoDeviceDetails(item),
            ProductTelemetryKind.LocalInstanceFailure =>
                item.Feature == "local-instance"
                && IsLocalInstanceOperation(item.Operation)
                && IsLocalInstanceFailureCode(item.Outcome)
                && IsLocalInstanceFailureCode(item.FailureCode)
                && item.Outcome == item.FailureCode
                && item.OperatingSystem is not null && OperatingSystemPattern().IsMatch(item.OperatingSystem)
                && item.DurationMilliseconds is >= 0 and <= 600_000
                && item.ConfigurationMode is "shared" or "isolated" or "not-applicable"
                && item.RunnerCount is >= 0 and <= 16
                && item.ProcessExitCode is null or >= 0 and <= 65_535
                && item.LogicalProcessorCount is null && item.GraphicsCapability is null
                && item.Material is null && item.Quantity is null
                && HasNoOcrSetupDetails(item) && HasNoDeviceDetails(item),
            ProductTelemetryKind.UiScaleCalibration =>
                item.Feature == "ui-scale" && item.Outcome == "observed"
                && item.DisplayWidth is >= 640 and <= 16_384
                && item.DisplayHeight is >= 480 and <= 16_384
                && item.InputScaleMilli is >= 800 and <= 1_200
                && item.RenderedScaleMilli is >= 500 and <= 1_500
                && item.DurationMilliseconds is null && item.Material is null && item.Quantity is null
                && item.OperatingSystem is null && item.LogicalProcessorCount is null
                && item.GraphicsCapability is null && item.HardwareModel is null
                && HasNoSetupDetails(item),
            _ => false,
        };
        if (!valid) throw new InvalidDataException("Telemetry event fields were invalid for its kind.");
    }

    private static bool HasNoMetrics(ProductTelemetryEvent item) =>
        item.DurationMilliseconds is null && item.Material is null && item.Quantity is null
        && item.OperatingSystem is null && item.LogicalProcessorCount is null
        && item.GraphicsCapability is null && HasNoExtendedDetails(item);

    private static bool HasNoExtendedDetails(ProductTelemetryEvent item) =>
        HasNoSetupDetails(item) && HasNoDeviceDetails(item);

    private static bool HasNoDeviceDetails(ProductTelemetryEvent item) =>
        item.HardwareModel is null && HasNoCalibrationDetails(item);

    private static bool HasNoCalibrationDetails(ProductTelemetryEvent item) =>
        item.DisplayWidth is null && item.DisplayHeight is null
        && item.InputScaleMilli is null && item.RenderedScaleMilli is null;

    public static bool IsHardwareModel(string value) =>
        value.Length is >= 1 and <= 96
        && (value == "unknown" || value.StartsWith("AMD ", StringComparison.Ordinal)
            || value.StartsWith("Intel ", StringComparison.Ordinal)
            || value.StartsWith("NVIDIA ", StringComparison.Ordinal)
            || value.StartsWith("Qualcomm ", StringComparison.Ordinal))
        && char.IsAsciiLetterOrDigit(value[0])
        && value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is ' ' or '.' or '_' or '+' or '-' or '(' or ')');

    private static bool HasNoSetupDetails(ProductTelemetryEvent item) =>
        item.SetupStage is null && item.RequestedDevice is null && item.ProcessExitCode is null
        && item.PythonLauncherPresent is null && item.WingetPresent is null
        && item.ExistingOcrPythonPresent is null && item.RuntimeMarkerPresent is null
        && item.Operation is null && item.FailureCode is null && item.ConfigurationMode is null
        && item.RunnerCount is null;

    private static bool HasNoOcrSetupDetails(ProductTelemetryEvent item) =>
        item.SetupStage is null && item.RequestedDevice is null
        && item.PythonLauncherPresent is null && item.WingetPresent is null
        && item.ExistingOcrPythonPresent is null && item.RuntimeMarkerPresent is null;

    private static bool HasNoLocalInstanceDetails(ProductTelemetryEvent item) =>
        item.Operation is null && item.FailureCode is null
        && item.ConfigurationMode is null && item.RunnerCount is null;

    [GeneratedRegex("^\\d+\\.\\d+\\.\\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionPattern();

    [GeneratedRegex("^windows-\\d{1,2}\\.\\d{1,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex OperatingSystemPattern();
}
