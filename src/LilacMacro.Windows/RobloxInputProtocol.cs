using LilacMacro.Core.Geometry;

namespace LilacMacro.Windows;

internal static class RobloxInputProtocol
{
    public const int ClickPositionSettleMilliseconds = 75;
    public const int ClickHoldMilliseconds = 20;
    public const int CursorParkingInsetPixels = 24;
    public const int HoverClearPulseCount = 4;
    public const int HoverClearPulseIntervalMilliseconds = 100;
    public const int HoverRenderSettleMilliseconds = 100;
    public const int InterKeyDelayMilliseconds = 25;
    public const int ShiftLockKeyHoldMilliseconds = 70;
    public const int ShiftLockSettleMilliseconds = 250;
    public const int QuickPlacementUnitKeyHoldMilliseconds = 110;
    public const int QuickPlacementUnitSelectionDelayMilliseconds = 250;
    public const int QuickPlacementClickCount = 3;
    public const int QuickPlacementBurstMilliseconds = 50;
    public const int CameraInputIncrementCount = 50;
    public const int CameraZoomWheelDelta = -5000;
    public const int CameraPitchDelta = 5000;
    public const int CameraMotionMilliseconds = 1000;
    public const int ScrollbarDragIncrementCount = 12;
    public const int ScrollbarDragMilliseconds = 180;

    public static (int First, int Second) RegisteredMotionDeltas(int x, int clientWidth)
    {
        int first = x < clientWidth - 1 ? 1 : -1;
        return (first, -first);
    }

    public static PixelPoint ParkingPoint(PixelSize clientSize)
    {
        int horizontalInset = Math.Min(CursorParkingInsetPixels, Math.Max(0, clientSize.Width - 2));
        int verticalInset = Math.Min(CursorParkingInsetPixels, Math.Max(0, clientSize.Height - 2));
        return new PixelPoint(
            Math.Max(0, clientSize.Width - 1 - horizontalInset),
            Math.Max(0, clientSize.Height - 1 - verticalInset));
    }

    public static int NextDistributedIncrement(int remaining, int incrementsRemaining)
    {
        if (incrementsRemaining < 1) throw new ArgumentOutOfRangeException(nameof(incrementsRemaining));
        return remaining / incrementsRemaining;
    }

    public static (int HoldMilliseconds, int GapMilliseconds) RapidClickTiming(
        int clickCount,
        int durationMilliseconds)
    {
        if (clickCount <= 0) throw new ArgumentOutOfRangeException(nameof(clickCount));
        if (durationMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(durationMilliseconds));
        if (clickCount == 1) return (durationMilliseconds, 0);

        int hold = Math.Min(
            ClickHoldMilliseconds,
            durationMilliseconds / (clickCount * 2));
        int gap = (durationMilliseconds - (hold * clickCount)) / (clickCount - 1);
        return (Math.Max(0, hold), Math.Max(0, gap));
    }
}
