using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Automation;

public enum ResourceRefuelTarget
{
    GoldMine,
    ResourceDrill,
}

public readonly record struct ResourceRefuelWalkStep(int VirtualKey, int HoldMilliseconds);

public readonly record struct ResourceRefuelDialogActions(PixelPoint Quantity, PixelPoint Confirm);

public static class ResourceRefuelPolicy
{
    public const int StationInteractionAttempts = 3;
    public const int ConfirmationAttempts = 3;
    public const string GoldMineRoute = "Gold Mine refuel";
    public const string ResourceDrillRoute = "Resource Drill refuel";
    public const string CombinedRoute = "Gold Mine + Resource Drill";

    public static IReadOnlyList<string> Routes { get; } =
    [
        CombinedRoute,
        GoldMineRoute,
        ResourceDrillRoute,
    ];

    private static readonly ResourceRefuelWalkStep[] GoldMineSteps =
    [
        new('W', 3000),
        new('A', 820),
        new('W', 2600),
    ];

    private static readonly ResourceRefuelWalkStep[] ResourceDrillSteps =
    [
        new('W', 3000),
        new('A', 750),
        new('W', 1000),
        new('A', 1600),
    ];

    public static IReadOnlyList<ResourceRefuelTarget> TargetsFor(string route) => route switch
    {
        GoldMineRoute => [ResourceRefuelTarget.GoldMine],
        ResourceDrillRoute => [ResourceRefuelTarget.ResourceDrill],
        CombinedRoute => [ResourceRefuelTarget.GoldMine, ResourceRefuelTarget.ResourceDrill],
        _ => throw new InvalidDataException($"Unknown utility route: {route}."),
    };

    public static IReadOnlyList<ResourceRefuelWalkStep> WalkFor(ResourceRefuelTarget target) => target switch
    {
        ResourceRefuelTarget.GoldMine => GoldMineSteps,
        ResourceRefuelTarget.ResourceDrill => ResourceDrillSteps,
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    public static TimeSpan StationObservationDelay(int attemptNumber)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptNumber, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(attemptNumber, StationInteractionAttempts);
        return TimeSpan.FromSeconds(attemptNumber * 2);
    }

    public static bool TryResolveDialogActions(
        PixelRect addFuel,
        PixelRect confirm,
        PixelRect cancel,
        PixelSize clientSize,
        out ResourceRefuelDialogActions actions)
    {
        actions = default;
        if (!addFuel.IsInside(clientSize) ||
            !confirm.IsInside(clientSize) ||
            !cancel.IsInside(clientSize)) return false;
        int separation = cancel.Center.X - confirm.Center.X;
        int rowDelta = Math.Abs(cancel.Center.Y - confirm.Center.Y);
        if (separation is < 150 or > 330 || rowDelta > 20) return false;

        int rowCenterY = (confirm.Center.Y + cancel.Center.Y) / 2;
        int dialogCenterX = (confirm.Center.X + cancel.Center.X) / 2;
        int centerTolerance = Math.Max(12, separation / 10);
        if (Math.Abs(addFuel.Center.X - dialogCenterX) > centerTolerance) return false;

        int verticalSeparation = addFuel.Center.Y - rowCenterY;
        if (verticalSeparation * 100 < separation * 52 ||
            verticalSeparation * 100 > separation * 75) return false;

        PixelPoint quantity = new(
            cancel.Center.X + (int)Math.Round(separation * 0.32),
            rowCenterY - (int)Math.Round(separation * 0.24));
        if (quantity.X < 0 || quantity.Y < 0 ||
            quantity.X >= clientSize.Width || quantity.Y >= clientSize.Height) return false;
        actions = new ResourceRefuelDialogActions(quantity, confirm.Center);
        return true;
    }
}
