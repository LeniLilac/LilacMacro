using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Automation;

public static class CodeRedemptionPolicy
{
    private const int LauncherOffsetX = -39;

    public const int RedeemAttempts = 3;
    public static readonly TimeSpan RedeemAttemptDelay = TimeSpan.FromSeconds(1);

    public static PixelPoint LauncherPoint(PixelPoint settingsGear, PixelSize clientSize)
    {
        PixelPoint point = new(checked(settingsGear.X + LauncherOffsetX), settingsGear.Y);
        if (point.X < 0 || point.Y < 0 || point.X >= clientSize.Width || point.Y >= clientSize.Height)
            throw new InvalidDataException("The verified Settings gear did not yield a safe Codes launcher point.");
        return point;
    }
}
