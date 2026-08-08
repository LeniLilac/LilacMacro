using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Ocr;

public enum TeamSwapViewport
{
    Top,
    Middle,
    Bottom,
}

public sealed record TeamSwapResolvedTarget(
    TeamSwapViewport Viewport,
    PixelPoint LoadPoint,
    PixelPoint? DragStart,
    PixelPoint? DragEnd,
    PixelPoint ScrollAnchor);

public sealed record TeamSwapCalibration(
    PixelSize ClientSize,
    PixelRect TitleBounds,
    PixelRect ScrollbarTopBounds,
    PixelRect ScrollbarBottomBounds,
    int RowPitch,
    PixelPoint ScrollAnchor,
    IReadOnlyList<PixelPoint> TopLoadPoints,
    IReadOnlyList<PixelPoint> BottomLoadPoints)
{
    private const int VisibleTeamsPerEndpoint = 3;

    public static TeamSwapCalibration? TryCreate(
        PixelSize clientSize,
        TeamSwapLayout top,
        TeamSwapLayout bottom,
        PixelRect topThumb,
        PixelRect bottomThumb)
    {
        if (!topThumb.IsInside(clientSize) || !bottomThumb.IsInside(clientSize) ||
            bottomThumb.Center.Y <= topThumb.Center.Y ||
            Math.Abs(bottomThumb.Center.X - topThumb.Center.X) > 6 ||
            Math.Abs(bottomThumb.Height - topThumb.Height) > 8 ||
            Math.Abs(bottom.RowPitch - top.RowPitch) > Math.Max(8, top.RowPitch / 8))
        {
            return null;
        }

        int pitch = checked((top.RowPitch + bottom.RowPitch) / 2);
        PixelPoint[] topLoads = ExpandEndpointLoads(top.LoadBounds, pitch, fromTop: true, clientSize);
        PixelPoint[] bottomLoads = ExpandEndpointLoads(bottom.LoadBounds, pitch, fromTop: false, clientSize);
        if (topLoads.Length != VisibleTeamsPerEndpoint ||
            bottomLoads.Length != VisibleTeamsPerEndpoint)
        {
            return null;
        }

        return new TeamSwapCalibration(
            clientSize,
            top.TitleBounds,
            topThumb,
            bottomThumb,
            pitch,
            top.ScrollAnchor.Bounds.Center,
            topLoads,
            bottomLoads);
    }

    public TeamSwapResolvedTarget? Resolve(int teamNumber, PixelRect currentTitle)
    {
        TeamSwapLayout.ValidateTeamNumber(teamNumber);
        if (currentTitle.Width <= 0 || currentTitle.Height <= 0) return null;
        double widthRatio = currentTitle.Width / (double)TitleBounds.Width;
        double heightRatio = currentTitle.Height / (double)TitleBounds.Height;
        if (widthRatio is < 0.82 or > 1.18 || heightRatio is < 0.72 or > 1.28)
            return null;

        TeamSwapViewport viewport;
        PixelPoint source;
        if (teamNumber <= 2)
        {
            viewport = TeamSwapViewport.Top;
            source = TopLoadPoints[teamNumber - 1];
        }
        else if (teamNumber >= 6)
        {
            viewport = TeamSwapViewport.Bottom;
            source = BottomLoadPoints[teamNumber - 6];
        }
        else
        {
            viewport = TeamSwapViewport.Middle;
            double rowOffset = teamNumber - 3.5;
            source = new PixelPoint(
                TopLoadPoints[0].X,
                checked(TopLoadPoints[0].Y +
                    (int)Math.Round(RowPitch * rowOffset, MidpointRounding.AwayFromZero)));
        }

        PixelPoint? dragStart = viewport == TeamSwapViewport.Middle
            ? Translate(ScrollbarTopBounds.Center, currentTitle)
            : null;
        PixelPoint? dragEnd = viewport == TeamSwapViewport.Middle
            ? Translate(new PixelPoint(
                ScrollbarTopBounds.Center.X,
                checked((ScrollbarTopBounds.Center.Y + ScrollbarBottomBounds.Center.Y) / 2)),
                currentTitle)
            : null;
        return new TeamSwapResolvedTarget(
            viewport,
            Translate(source, currentTitle),
            dragStart,
            dragEnd,
            Translate(ScrollAnchor, currentTitle));
    }

    private PixelPoint Translate(PixelPoint point, PixelRect currentTitle) => new(
        checked(point.X + currentTitle.X - TitleBounds.X),
        checked(point.Y + currentTitle.Y - TitleBounds.Y));

    private static PixelPoint[] ExpandEndpointLoads(
        IReadOnlyList<PixelRect> loads,
        int pitch,
        bool fromTop,
        PixelSize clientSize)
    {
        PixelPoint[] points = loads.Select(load => load.Center).OrderBy(point => point.Y).ToArray();
        if (points.Length < 2) return [];
        int x = checked((int)Math.Round(points.Average(point => point.X)));
        int firstY = fromTop
            ? points[0].Y
            : points[^1].Y - pitch * 2;
        PixelPoint[] expanded = Enumerable.Range(0, VisibleTeamsPerEndpoint)
            .Select(index => new PixelPoint(x, checked(firstY + index * pitch)))
            .ToArray();
        return expanded.All(point =>
                point.X >= 0 && point.X < clientSize.Width &&
                point.Y >= 0 && point.Y < clientSize.Height)
            ? expanded
            : [];
    }
}
