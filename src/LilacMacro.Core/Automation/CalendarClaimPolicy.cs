using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.Core.Automation;

public static class CalendarClaimPolicy
{
    public const int Passes = 3;

    public static bool TryResolveClaimPoints(
        IReadOnlyList<OcrTextRegion> regions,
        PixelSize clientSize,
        out IReadOnlyList<PixelPoint> points)
    {
        ArgumentNullException.ThrowIfNull(regions);
        OcrTargetMatch[] days = Enumerable.Range(1, 7)
            .Select(day => OcrRuleEngine.FindExactTarget(
                new OcrTargetRule($"Day {day}", $"day {day}"), regions))
            .Where(match => match is not null)
            .Cast<OcrTargetMatch>()
            .ToArray();
        if (days.Length != 7)
        {
            points = [];
            return false;
        }

        int[] columns = Cluster(days.Select(day => day.Region.Bounds.X), tolerance: 24);
        int[] rows = Cluster(days.Select(day => day.Region.Bounds.Y), tolerance: 24);
        if (columns.Length != 4 || rows.Length != 2)
        {
            points = [];
            return false;
        }
        int columnStep = columns.Zip(columns.Skip(1), (left, right) => right - left).Min();
        int rowStep = rows[1] - rows[0];
        if (columnStep is < 120 or > 260 || rowStep is < 120 or > 260)
        {
            points = [];
            return false;
        }

        PixelPoint[] resolved = days
            .OrderByDescending(day => ParseDay(day.Target))
            .Select(day => new PixelPoint(
                day.Region.Bounds.Center.X + columnStep / 3,
                day.Region.Bounds.Center.Y + rowStep * 2 / 5))
            .ToArray();
        if (resolved.Any(point => point.X < 0 || point.Y < 0 || point.X >= clientSize.Width || point.Y >= clientSize.Height))
        {
            points = [];
            return false;
        }
        points = resolved;
        return true;
    }

    private static int ParseDay(string target) => int.Parse(target.AsSpan(4));

    private static int[] Cluster(IEnumerable<int> values, int tolerance)
    {
        List<List<int>> groups = [];
        foreach (int value in values.Order())
        {
            if (groups.Count == 0 || value - groups[^1].Average() > tolerance) groups.Add([value]);
            else groups[^1].Add(value);
        }
        return groups.Select(group => (int)Math.Round(group.Average())).ToArray();
    }
}
