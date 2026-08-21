using System.Text.RegularExpressions;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Core.Automation;

public enum TowerType
{
    Trait,
    Traitless,
}

public sealed record TowerFloorSelection(int Floor, OcrTextRegion Region);

public readonly record struct TowerTerminalState(
    int Progress,
    int DefeatsOnFloor,
    bool ShouldStop,
    bool ShouldRepeatFloor);

public static partial class TowerRunPolicy
{
    public const int DefaultDefeatsBeforeStop = 5;
    public const int MaximumModeRevealScrollAttempts = 3;
    public const int ModeRevealScrollWheelDelta = -5000;
    public const int ModeRevealScrollMilliseconds = 280;
    public const int ModeRevealSettleMilliseconds = 250;
    public const int MaximumModeTransitionActionAttempts = 1;
    public const string TraitRoute = "Trait Tower";
    public const string TraitlessRoute = "Traitless Tower";
    public const string TraitPlacementRouteId = "trait-tower";
    public const string TraitlessPlacementRouteId = "traitless-tower";

    public static TowerType ParseType(string route) => route switch
    {
        TraitRoute => TowerType.Trait,
        TraitlessRoute => TowerType.Traitless,
        _ => throw new InvalidDataException($"Unsupported Tower type: {route}."),
    };

    public static string SelectionLabel(TowerType type) => type switch
    {
        TowerType.Trait => "Tower",
        TowerType.Traitless => TraitlessRoute,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static string PlacementRouteId(TowerType type) => type switch
    {
        TowerType.Trait => TraitPlacementRouteId,
        TowerType.Traitless => TraitlessPlacementRouteId,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static PixelPoint ModeRevealScrollAnchor(PixelSize clientSize)
    {
        if (clientSize.Width < 1 || clientSize.Height < 1)
            throw new ArgumentOutOfRangeException(nameof(clientSize));
        return new PixelPoint(clientSize.Width / 2, clientSize.Height / 2);
    }

    public static TowerFloorSelection? SelectTopRightFloor(IReadOnlyList<OcrTextRegion> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        return regions
            .Select(region => OcrRuleEngine.Normalize(region.Text).StartsWith("floor", StringComparison.Ordinal)
                ? new TowerFloorSelection(TryParseFloor(region.Text, out int floor) ? floor : 0, region)
                : null)
            .Where(candidate => candidate is not null)
            .Cast<TowerFloorSelection>()
            .OrderBy(candidate => candidate.Region.Bounds.Y)
            .ThenByDescending(candidate => candidate.Region.Bounds.X)
            .ThenByDescending(candidate => candidate.Region.RecognitionConfidence)
            .FirstOrDefault();
    }

    public static bool TryParseFloor(string? text, out int floor)
    {
        Match match = FloorPattern().Match(text ?? string.Empty);
        return int.TryParse(match.Groups[1].Value, out floor) && floor > 0;
    }

    public static bool ShouldStopAfterDefeat(int consecutiveDefeats, int defeatsBeforeStop)
    {
        if (consecutiveDefeats < 0) throw new ArgumentOutOfRangeException(nameof(consecutiveDefeats));
        if (defeatsBeforeStop < 1) throw new ArgumentOutOfRangeException(nameof(defeatsBeforeStop));
        return consecutiveDefeats >= defeatsBeforeStop;
    }

    public static TowerTerminalState ApplyTerminalOutcome(
        bool victory,
        int currentProgress,
        int defeatsOnFloor,
        int verifiedFloor,
        int defeatsBeforeStop)
    {
        if (currentProgress < 0) throw new ArgumentOutOfRangeException(nameof(currentProgress));
        if (defeatsOnFloor < 0) throw new ArgumentOutOfRangeException(nameof(defeatsOnFloor));
        if (verifiedFloor < 1) throw new InvalidDataException("Tower outcome has no verified floor.");
        if (defeatsBeforeStop < 1) throw new ArgumentOutOfRangeException(nameof(defeatsBeforeStop));
        if (victory)
            return new TowerTerminalState(Math.Max(currentProgress, verifiedFloor), 0, false, false);

        int nextDefeats = checked(defeatsOnFloor + 1);
        bool stop = ShouldStopAfterDefeat(nextDefeats, defeatsBeforeStop);
        return new TowerTerminalState(currentProgress, nextDefeats, stop, !stop);
    }

    [GeneratedRegex(@"\bfloor\s*(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FloorPattern();
}
