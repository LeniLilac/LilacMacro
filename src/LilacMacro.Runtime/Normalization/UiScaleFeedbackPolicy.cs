using System.Globalization;

namespace LilacMacro.Runtime.Normalization;

internal static class UiScaleFeedbackPolicy
{
    public const double MinimumValue = 0.80;
    public const double MaximumValue = 1.20;
    public const double TargetRenderedScale = 1.00;

    public static double Correct(double appliedValue, double observedRenderedScale)
    {
        if (!double.IsFinite(appliedValue) || appliedValue <= 0)
            throw new ArgumentOutOfRangeException(nameof(appliedValue));
        if (!double.IsFinite(observedRenderedScale) || observedRenderedScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(observedRenderedScale));

        double corrected = appliedValue * TargetRenderedScale / observedRenderedScale;
        return Math.Round(
            Math.Clamp(corrected, MinimumValue, MaximumValue),
            2,
            MidpointRounding.AwayFromZero);
    }

    public static string Format(double value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);
}
