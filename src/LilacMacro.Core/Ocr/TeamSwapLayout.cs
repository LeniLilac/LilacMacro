using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Ocr;

public enum TeamSwapButtonKind
{
    Save,
    Load,
}

public sealed record TeamSwapButton(TeamSwapButtonKind Kind, PixelRect Bounds);

public sealed record TeamSwapVisibleRow(
    int VisibleIndex,
    PixelRect SaveBounds,
    PixelRect LoadBounds);

public sealed record TeamSwapLayout(
    PixelRect TitleBounds,
    IReadOnlyList<TeamSwapButton> Buttons,
    IReadOnlyList<TeamSwapVisibleRow> Rows,
    int RowPitch)
{
    public const int MinimumTeamNumber = 1;
    public const int MaximumTeamNumber = 8;
    private const double PairOffsetRatio = 0.43;
    private const double PairToleranceRatio = 0.22;

    public TeamSwapButton ScrollAnchor => Buttons
        .OrderBy(button => Math.Abs(button.Bounds.Center.Y - 350))
        .ThenBy(button => button.Kind)
        .First();

    public IReadOnlyList<PixelRect> LoadBounds => Rows
        .Select(row => row.LoadBounds)
        .OrderBy(bounds => bounds.Center.Y)
        .ToArray();

    public static TeamSwapLayout? TryCreate(
        IReadOnlyList<OcrTextRegion> regions,
        PixelSize clientSize)
    {
        ArgumentNullException.ThrowIfNull(regions);
        IReadOnlyList<OcrTextRegion> candidates = OcrRegionComposer.AddAdjacentPairs(regions);
        OcrTextRegion? title = candidates
            .Where(region => OcrRuleEngine.Normalize(region.Text)
                .Contains("unitteams", StringComparison.Ordinal))
            .OrderBy(region => region.Bounds.Y)
            .ThenBy(region => region.Bounds.X)
            .FirstOrDefault();
        if (title is null || !title.Bounds.IsInside(clientSize)) return null;

        TeamSwapButton[] buttons = BuildButtons(regions)
            .Where(button => button.Bounds.IsInside(clientSize))
            .OrderBy(button => button.Bounds.Center.Y)
            .ThenBy(button => button.Kind)
            .ToArray();
        TeamSwapButton[] saves = buttons
            .Where(button => button.Kind == TeamSwapButtonKind.Save)
            .ToArray();
        TeamSwapButton[] loads = buttons
            .Where(button => button.Kind == TeamSwapButtonKind.Load)
            .ToArray();
        if (saves.Length == 0 || loads.Length == 0) return null;

        int rowPitch = EstimateRowPitch(buttons, title.Bounds);
        List<TeamSwapVisibleRow> rows = [];
        HashSet<TeamSwapButton> claimedLoads = [];
        foreach (TeamSwapButton save in saves)
        {
            TeamSwapButton? load = loads
                .Where(candidate => !claimedLoads.Contains(candidate))
                .Where(candidate => candidate.Bounds.Center.Y > save.Bounds.Center.Y)
                .Where(candidate => Math.Abs(candidate.Bounds.Center.X - save.Bounds.Center.X) <= rowPitch * 0.35)
                .Select(candidate => new
                {
                    Button = candidate,
                    Error = Math.Abs(
                        candidate.Bounds.Center.Y -
                        (save.Bounds.Center.Y + rowPitch * PairOffsetRatio)),
                })
                .Where(candidate => candidate.Error <= rowPitch * PairToleranceRatio)
                .OrderBy(candidate => candidate.Error)
                .Select(candidate => candidate.Button)
                .FirstOrDefault();
            if (load is null) continue;
            claimedLoads.Add(load);
            rows.Add(new TeamSwapVisibleRow(
                rows.Count + 1,
                save.Bounds,
                load.Bounds));
        }
        if (rows.Count < 2) return null;

        return new TeamSwapLayout(title.Bounds, buttons, rows, rowPitch);
    }

    public static void ValidateTeamNumber(int teamNumber)
    {
        if (teamNumber is < MinimumTeamNumber or > MaximumTeamNumber)
            throw new ArgumentOutOfRangeException(nameof(teamNumber));
    }

    private static IReadOnlyList<TeamSwapButton> BuildButtons(
        IReadOnlyList<OcrTextRegion> regions)
    {
        List<TeamSwapButton> buttons = [];
        IReadOnlyList<OcrTextRegion> candidates = OcrRegionComposer.AddAdjacentPairs(regions);
        foreach (OcrTextRegion region in candidates
            .OrderByDescending(candidate => IsWholeButtonText(candidate.Text))
            .ThenByDescending(candidate => candidate.Bounds.Width))
        {
            string normalized = OcrRuleEngine.Normalize(region.Text);
            TeamSwapButtonKind? kind = normalized switch
            {
                "save" or "saveteam" => TeamSwapButtonKind.Save,
                "load" or "loadteam" => TeamSwapButtonKind.Load,
                _ => null,
            };
            if (kind is null) continue;

            PixelRect bounds = region.Bounds;
            if (normalized is "save" or "load")
            {
                OcrTextRegion? team = FindFollowingTeamToken(region, regions);
                if (team is not null) bounds = PixelRect.Union(bounds, team.Bounds);
            }
            if (!buttons.Any(existing =>
                    existing.Kind == kind &&
                    Distance(existing.Bounds.Center, bounds.Center) <= Math.Max(bounds.Height, 12)))
            {
                buttons.Add(new TeamSwapButton(kind.Value, bounds));
            }
        }
        return buttons;
    }

    private static bool IsWholeButtonText(string text) =>
        OcrRuleEngine.Normalize(text) is "saveteam" or "loadteam";

    private static OcrTextRegion? FindFollowingTeamToken(
        OcrTextRegion first,
        IReadOnlyList<OcrTextRegion> regions) => regions
        .Where(region => OcrRuleEngine.Normalize(region.Text) == "team")
        .Where(region => region.Bounds.X >= first.Bounds.Right - 3)
        .Where(region => region.Bounds.X - first.Bounds.Right <= Math.Max(32, first.Bounds.Height * 2))
        .Where(region => Math.Abs(region.Bounds.Center.Y - first.Bounds.Center.Y) <=
            Math.Max(first.Bounds.Height, region.Bounds.Height))
        .OrderBy(region => region.Bounds.X)
        .FirstOrDefault();

    private static int EstimateRowPitch(
        IReadOnlyList<TeamSwapButton> buttons,
        PixelRect titleBounds)
    {
        int[] gaps = buttons
            .GroupBy(button => button.Kind)
            .SelectMany(group => group
                .Select(button => button.Bounds.Center.Y)
                .Order()
                .Zip(
                    group.Select(button => button.Bounds.Center.Y).Order().Skip(1),
                    (first, second) => second - first))
            .Where(gap => gap is >= 70 and <= 230)
            .Order()
            .ToArray();
        if (gaps.Length == 0)
            return checked((int)Math.Round(titleBounds.Width * 1.34, MidpointRounding.AwayFromZero));
        return gaps.Length % 2 == 1
            ? gaps[gaps.Length / 2]
            : checked((gaps[gaps.Length / 2 - 1] + gaps[gaps.Length / 2]) / 2);
    }

    private static double Distance(PixelPoint first, PixelPoint second)
    {
        int dx = first.X - second.X;
        int dy = first.Y - second.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
