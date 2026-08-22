namespace LilacMacro.Windows.Capture;

internal readonly record struct CaptureColorContext
{
    public const float SceneReferenceWhiteNits = 80f;
    public const float AdvancedColorFallbackWhiteNits = 203f;

    public static CaptureColorContext StandardSdr { get; } = new(
        false,
        SceneReferenceWhiteNits,
        SceneReferenceWhiteNits,
        "RgbFullG22NoneP709",
        "sdr-color-space",
        false);

    public CaptureColorContext(
        bool advancedColorActive,
        float sdrWhiteLevelNits,
        float displayMaxLuminanceNits,
        string outputColorSpace = "unknown",
        string detection = "caller-supplied",
        bool usedSdrWhiteFallback = false)
    {
        AdvancedColorActive = advancedColorActive;
        SdrWhiteLevelNits = IsPositiveFinite(sdrWhiteLevelNits) ? sdrWhiteLevelNits : 80f;
        DisplayMaxLuminanceNits = IsPositiveFinite(displayMaxLuminanceNits)
            ? Math.Max(displayMaxLuminanceNits, SdrWhiteLevelNits)
            : Math.Max(1000f, SdrWhiteLevelNits);
        OutputColorSpace = string.IsNullOrWhiteSpace(outputColorSpace) ? "unknown" : outputColorSpace;
        Detection = string.IsNullOrWhiteSpace(detection) ? "unknown" : detection;
        UsedSdrWhiteFallback = usedSdrWhiteFallback;
    }

    public bool AdvancedColorActive { get; }

    public float SdrWhiteLevelNits { get; }

    public float DisplayMaxLuminanceNits { get; }

    public string OutputColorSpace { get; }

    public string Detection { get; }

    public bool UsedSdrWhiteFallback { get; }

    public float ScRgbReferenceScale => AdvancedColorActive
        ? SceneReferenceWhiteNits / SdrWhiteLevelNits
        : 1f;

    public float RelativeDisplayPeak => AdvancedColorActive
        ? Math.Clamp(DisplayMaxLuminanceNits / SdrWhiteLevelNits, 1.25f, 12.5f)
        : 1f;

    public CaptureColorDiagnostics ToDiagnostics() => new(
        "R16G16B16A16Float",
        OutputColorSpace,
        AdvancedColorActive,
        SdrWhiteLevelNits,
        DisplayMaxLuminanceNits,
        ScRgbReferenceScale,
        Detection,
        UsedSdrWhiteFallback);

    private static bool IsPositiveFinite(float value) => float.IsFinite(value) && value > 0f;
}

public sealed record CaptureColorDiagnostics(
    string PixelFormat,
    string OutputColorSpace,
    bool AdvancedColorActive,
    float SdrWhiteLevelNits,
    float DisplayMaxLuminanceNits,
    float ScRgbReferenceScale,
    string Detection,
    bool UsedSdrWhiteFallback);
