using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Ocr;

public enum TeamSwapViewport
{
    Top,
    Middle,
    Bottom,
}

public enum TeamSwapRetryGeometryDecision
{
    Block,
    RetainCalibration,
    Recalibrate,
}

public sealed record TeamSwapResolvedTarget(
    TeamSwapViewport Viewport,
    PixelPoint LoadPoint,
    PixelPoint? DragStart,
    PixelPoint? DragEnd,
    int MiddleWheelUnits,
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
    private const int TotalTeams = 8;
    private const int VisibleTeamsPerEndpoint = 3;
    private const double ClippedTopLoadLiftRatio = 0.125;
    public const double MiddleTargetPosition = 0.5;
    public int MiddleWheelUnits { get; init; }

    public static int? EstimateMiddleWheelUnits(
        int probeWheelUnits,
        double observedNormalizedPosition)
    {
        if (probeWheelUnits <= 0 ||
            !double.IsFinite(observedNormalizedPosition) ||
            observedNormalizedPosition is <= 0.05 or >= 0.9)
        {
            return null;
        }

        double estimate = probeWheelUnits * MiddleTargetPosition / observedNormalizedPosition;
        if (!double.IsFinite(estimate) || estimate is < 60 or > 10000) return null;
        return checked((int)Math.Round(estimate, MidpointRounding.AwayFromZero));
    }

    public static bool IsMiddlePositionUsable(double normalizedPosition) =>
        double.IsFinite(normalizedPosition) && normalizedPosition is >= 0.4 and <= 0.6;

    public static bool IsTopPositionUsable(double normalizedPosition) =>
        double.IsFinite(normalizedPosition) && normalizedPosition is >= 0 and <= 0.08;

    public static TeamSwapRetryGeometryDecision DecideRetryGeometry(
        bool sourceMatches,
        bool layoutAvailable,
        bool topThumbValid,
        bool targetValid)
    {
        if (!sourceMatches || !layoutAvailable) return TeamSwapRetryGeometryDecision.Block;
        return topThumbValid && targetValid
            ? TeamSwapRetryGeometryDecision.RetainCalibration
            : TeamSwapRetryGeometryDecision.Recalibrate;
    }

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

    public TeamSwapResolvedTarget? Resolve(
        int teamNumber,
        PixelRect currentTitle,
        double middleNormalizedPosition = MiddleTargetPosition)
    {
        TeamSwapLayout.ValidateTeamNumber(teamNumber);
        if (!currentTitle.IsInside(ClientSize)) return null;
        int maximumPositionDrift = Math.Max(12, RowPitch / 5);
        if (Math.Abs(currentTitle.X - TitleBounds.X) > maximumPositionDrift ||
            Math.Abs(currentTitle.Y - TitleBounds.Y) > maximumPositionDrift)
        {
            return null;
        }

        TeamSwapViewport viewport;
        PixelPoint source;
        if (teamNumber <= 3)
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
            if (!IsMiddlePositionUsable(middleNormalizedPosition)) return null;
            viewport = TeamSwapViewport.Middle;
            double rowOffset = teamNumber - 1 -
                middleNormalizedPosition * (TotalTeams - VisibleTeamsPerEndpoint);
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
            viewport == TeamSwapViewport.Middle ? MiddleWheelUnits : 0,
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
        if (fromTop)
        {
            // The third Load control is clipped by the viewport. Its OCR text center is
            // below the interactive area, but the upper green edge remains clickable.
            int lift = Math.Max(2, checked((int)Math.Round(
                pitch * ClippedTopLoadLiftRatio,
                MidpointRounding.AwayFromZero)));
            PixelPoint third = expanded[^1];
            expanded[^1] = new PixelPoint(third.X, checked(third.Y - lift));
        }
        return expanded.All(point =>
                point.X >= 0 && point.X < clientSize.Width &&
                point.Y >= 0 && point.Y < clientSize.Height)
            ? expanded
            : [];
    }
}
