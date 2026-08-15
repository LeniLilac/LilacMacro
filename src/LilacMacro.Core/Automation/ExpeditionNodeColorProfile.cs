namespace LilacMacro.Core.Automation;

public sealed record ExpeditionNodeColorProfile
{
    public const int CurrentVersion = 2;

    public int Version { get; init; } = CurrentVersion;
    public Dictionary<ExpeditionNodeType, double> Hues { get; init; } = [];

    public bool IsComplete => Enum.GetValues<ExpeditionNodeType>()
        .All(Hues.ContainsKey);

    public ExpeditionNodeType? Classify(double hue, double maximumDistance = 8, double minimumMargin = 2)
    {
        (ExpeditionNodeType Node, double Distance)[] ranked = Hues
            .Select(pair => (pair.Key, HueDistance(hue, pair.Value)))
            .OrderBy(pair => pair.Item2)
            .ToArray();
        if (ranked.Length == 0 || ranked[0].Distance > maximumDistance) return null;
        if (ranked.Length > 1 && ranked[1].Distance - ranked[0].Distance < minimumMargin) return null;
        return ranked[0].Node;
    }

    public void Learn(ExpeditionNodeType node, double hue)
    {
        if (!double.IsFinite(hue) || hue is < 0 or >= 180) throw new ArgumentOutOfRangeException(nameof(hue));
        Hues[node] = Hues.TryGetValue(node, out double prior)
            ? CircularMean(prior, hue)
            : hue;
    }

    public static double HueDistance(double left, double right)
    {
        double difference = Math.Abs(left - right) % 180;
        return Math.Min(difference, 180 - difference);
    }

    private static double CircularMean(double left, double right)
    {
        double leftRadians = left * Math.PI / 90;
        double rightRadians = right * Math.PI / 90;
        double angle = Math.Atan2(Math.Sin(leftRadians) + Math.Sin(rightRadians),
            Math.Cos(leftRadians) + Math.Cos(rightRadians));
        if (angle < 0) angle += Math.PI * 2;
        return angle * 90 / Math.PI;
    }
}
