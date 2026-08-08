namespace LilacMacro.Windows.Capture;

internal readonly record struct CaptureColorContext
{
    public const float SceneReferenceWhiteNits = 80f;

    public static CaptureColorContext StandardSdr { get; } = new(false, 80f, 80f);

    public CaptureColorContext(bool advancedColorActive, float sdrWhiteLevelNits, float displayMaxLuminanceNits)
    {
        AdvancedColorActive = advancedColorActive;
        SdrWhiteLevelNits = IsPositiveFinite(sdrWhiteLevelNits) ? sdrWhiteLevelNits : 80f;
        DisplayMaxLuminanceNits = IsPositiveFinite(displayMaxLuminanceNits)
            ? Math.Max(displayMaxLuminanceNits, SdrWhiteLevelNits)
            : Math.Max(1000f, SdrWhiteLevelNits);
    }

    public bool AdvancedColorActive { get; }

    public float SdrWhiteLevelNits { get; }

    public float DisplayMaxLuminanceNits { get; }

    public float ScRgbReferenceScale => AdvancedColorActive
        ? SceneReferenceWhiteNits / SdrWhiteLevelNits
        : 1f;

    public float RelativeDisplayPeak => AdvancedColorActive
        ? Math.Clamp(DisplayMaxLuminanceNits / SdrWhiteLevelNits, 1.25f, 12.5f)
        : 1f;

    private static bool IsPositiveFinite(float value) => float.IsFinite(value) && value > 0f;
}
