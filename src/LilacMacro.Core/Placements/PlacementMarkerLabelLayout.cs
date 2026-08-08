using LilacMacro.Core.Geometry;

namespace LilacMacro.Core.Placements;

public readonly record struct PlacementMarkerLabelRequest
{
    public PlacementMarkerLabelRequest(
        Guid key,
        int anchorX,
        int anchorY,
        int labelWidth,
        int labelHeight)
    {
        if (labelWidth <= 0 || labelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(labelWidth),
                "Marker labels must have positive dimensions.");
        }

        Key = key;
        AnchorX = anchorX;
        AnchorY = anchorY;
        LabelWidth = labelWidth;
        LabelHeight = labelHeight;
    }

    public Guid Key { get; }

    public int AnchorX { get; }

    public int AnchorY { get; }

    public int LabelWidth { get; }

    public int LabelHeight { get; }
}

public readonly record struct PlacementMarkerLabelPlacement(
    Guid Key,
    PixelRect LabelBounds,
    PlacementMarkerConnector Connector);

public readonly record struct PlacementMarkerConnector(
    int BendX,
    int BendY,
    int EndX,
    int EndY);

public static class PlacementMarkerLabelLayout
{
    private const int EdgeMargin = 4;
    private const int LabelGap = 4;
    private const int PointRadius = 7;
    private const int PreferredHorizontalLanes = 4;
    private const int ConnectorClearance = 2;

    public static IReadOnlyList<PlacementMarkerLabelPlacement> Arrange(
        IReadOnlyCollection<PlacementMarkerLabelRequest> requests,
        int surfaceWidth,
        int surfaceHeight)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (surfaceWidth <= EdgeMargin * 2 || surfaceHeight <= EdgeMargin * 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(surfaceWidth),
                "The marker surface is too small.");
        }

        PlacementMarkerLabelRequest[] items = requests.ToArray();
        Validate(items, surfaceWidth, surfaceHeight);
        PixelRect[] pointExclusions = items.Select(PointExclusion).ToArray();
        List<PlacementMarkerLabelPlacement> placements = new(items.Length);
        List<PixelRect> occupiedLabels = new(items.Length);
        List<PixelRect> occupiedConnectors = new(items.Length * 2);

        foreach (PlacementMarkerLabelRequest item in items
                     .OrderBy(value => value.AnchorY)
                     .ThenBy(value => value.AnchorX)
                     .ThenBy(value => value.Key))
        {
            CandidateRoute[] candidates = Candidates(item, surfaceWidth, surfaceHeight)
                .Distinct()
                .SelectMany(label => ConnectorRoutes(item, label)
                    .Select(connector => new CandidateRoute(label, connector)))
                .ToArray();
            CandidateRoute selected = candidates.FirstOrDefault(candidate =>
                !HasCollision(
                    item,
                    candidate,
                    pointExclusions,
                    occupiedLabels,
                    occupiedConnectors));
            if (selected.Label.Width == 0)
            {
                selected = candidates
                    .OrderBy(candidate => CollisionArea(
                        item,
                        candidate,
                        pointExclusions,
                        occupiedLabels,
                        occupiedConnectors))
                    .ThenBy(candidate => DistanceFromPreferred(item, candidate.Label))
                    .First();
            }

            placements.Add(new PlacementMarkerLabelPlacement(item.Key, selected.Label, selected.Connector));
            occupiedLabels.Add(selected.Label);
            occupiedConnectors.AddRange(ConnectorRegions(item, selected.Connector));
        }

        return placements;
    }

    private static void Validate(
        IReadOnlyCollection<PlacementMarkerLabelRequest> requests,
        int surfaceWidth,
        int surfaceHeight)
    {
        if (requests.Select(request => request.Key).Distinct().Count() != requests.Count)
        {
            throw new ArgumentException("Marker layout keys must be unique.", nameof(requests));
        }

        foreach (PlacementMarkerLabelRequest request in requests)
        {
            if (request.AnchorX < 0 || request.AnchorX >= surfaceWidth ||
                request.AnchorY < 0 || request.AnchorY >= surfaceHeight)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requests),
                    "Marker anchors must remain inside the surface.");
            }

            if (request.LabelWidth > surfaceWidth - EdgeMargin * 2 ||
                request.LabelHeight > surfaceHeight - EdgeMargin * 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requests),
                    "Marker labels must fit inside the surface.");
            }
        }
    }

    private static IEnumerable<PixelRect> Candidates(
        PlacementMarkerLabelRequest request,
        int surfaceWidth,
        int surfaceHeight)
    {
        int horizontalStep = request.LabelWidth + LabelGap;
        int verticalStep = request.LabelHeight + LabelGap;
        int rightStart = request.AnchorX + PointRadius + LabelGap;
        int leftStart = request.AnchorX - PointRadius - LabelGap - request.LabelWidth;
        int aboveStart = request.AnchorY - PointRadius - LabelGap - request.LabelHeight;
        int belowStart = request.AnchorY + PointRadius + LabelGap;
        int horizontalLanes = Math.Max(
            PreferredHorizontalLanes,
            surfaceWidth / horizontalStep + 1);
        int verticalLanes = surfaceHeight / verticalStep + 1;

        for (int verticalLane = 0; verticalLane < verticalLanes; verticalLane++)
        {
            int[] tops =
            [
                aboveStart - verticalLane * verticalStep,
                belowStart + verticalLane * verticalStep,
            ];
            foreach (int top in tops)
            {
                for (int horizontalLane = 0; horizontalLane < horizontalLanes; horizontalLane++)
                {
                    int distance = horizontalLane * horizontalStep;
                    PixelRect right = new(
                        rightStart + distance,
                        top,
                        request.LabelWidth,
                        request.LabelHeight);
                    if (FitsWithMargin(right, surfaceWidth, surfaceHeight)) yield return right;

                    PixelRect left = new(
                        leftStart - distance,
                        top,
                        request.LabelWidth,
                        request.LabelHeight);
                    if (FitsWithMargin(left, surfaceWidth, surfaceHeight)) yield return left;
                }
            }
        }
    }

    private static bool FitsWithMargin(PixelRect region, int width, int height) =>
        region.X >= EdgeMargin &&
        region.Y >= EdgeMargin &&
        region.Right <= width - EdgeMargin &&
        region.Bottom <= height - EdgeMargin;

    private static PixelRect PointExclusion(PlacementMarkerLabelRequest request)
    {
        int radius = PointRadius + LabelGap;
        return new PixelRect(
            request.AnchorX - radius,
            request.AnchorY - radius,
            radius * 2 + 1,
            radius * 2 + 1);
    }

    private static bool HasCollision(
        PlacementMarkerLabelRequest request,
        CandidateRoute candidate,
        IReadOnlyCollection<PixelRect> pointExclusions,
        IReadOnlyCollection<PixelRect> occupiedLabels,
        IReadOnlyCollection<PixelRect> occupiedConnectors) =>
        pointExclusions.Any(region => Intersects(candidate.Label, region)) ||
        occupiedLabels.Any(region => Intersects(candidate.Label, Expand(region, LabelGap))) ||
        occupiedConnectors.Any(region => Intersects(candidate.Label, Expand(region, LabelGap))) ||
        ConnectorRegions(request, candidate.Connector).Any(segment =>
            pointExclusions
                .Where(region => region != PointExclusion(request))
                .Any(region => Intersects(segment, region)) ||
            occupiedLabels.Any(region => Intersects(segment, region)) ||
            occupiedConnectors.Any(region => Intersects(segment, region)));

    private static long CollisionArea(
        PlacementMarkerLabelRequest request,
        CandidateRoute candidate,
        IReadOnlyCollection<PixelRect> pointExclusions,
        IReadOnlyCollection<PixelRect> occupiedLabels,
        IReadOnlyCollection<PixelRect> occupiedConnectors) =>
        pointExclusions.Sum(region => OverlapArea(candidate.Label, region)) +
        occupiedLabels.Sum(region => OverlapArea(candidate.Label, Expand(region, LabelGap))) +
        occupiedConnectors.Sum(region => OverlapArea(candidate.Label, Expand(region, LabelGap))) +
        ConnectorRegions(request, candidate.Connector).Sum(segment =>
            pointExclusions
                .Where(region => region != PointExclusion(request))
                .Sum(region => OverlapArea(segment, region)) +
            occupiedLabels.Sum(region => OverlapArea(segment, region)) +
            occupiedConnectors.Sum(region => OverlapArea(segment, region)));

    private static IEnumerable<PlacementMarkerConnector> ConnectorRoutes(
        PlacementMarkerLabelRequest request,
        PixelRect label)
    {
        int centerX = label.X + label.Width / 2;
        int centerY = label.Y + label.Height / 2;
        int sideX = label.Right <= request.AnchorX ? label.Right : label.X;
        int verticalEdgeY = label.Bottom <= request.AnchorY ? label.Bottom : label.Y;

        yield return new PlacementMarkerConnector(request.AnchorX, centerY, sideX, centerY);
        yield return new PlacementMarkerConnector(centerX, request.AnchorY, centerX, verticalEdgeY);
    }

    private static IEnumerable<PixelRect> ConnectorRegions(
        PlacementMarkerLabelRequest request,
        PlacementMarkerConnector connector)
    {
        yield return SegmentRegion(
            request.AnchorX,
            request.AnchorY,
            connector.BendX,
            connector.BendY);
        yield return SegmentRegion(
            connector.BendX,
            connector.BendY,
            connector.EndX,
            connector.EndY);
    }

    private static PixelRect SegmentRegion(int startX, int startY, int endX, int endY)
    {
        int left = Math.Min(startX, endX) - ConnectorClearance;
        int top = Math.Min(startY, endY) - ConnectorClearance;
        int right = Math.Max(startX, endX) + ConnectorClearance + 1;
        int bottom = Math.Max(startY, endY) + ConnectorClearance + 1;
        return new PixelRect(left, top, right - left, bottom - top);
    }

    private static long DistanceFromPreferred(
        PlacementMarkerLabelRequest request,
        PixelRect candidate)
    {
        int preferredX = request.AnchorX + PointRadius + LabelGap;
        int preferredY = request.AnchorY - PointRadius - LabelGap - request.LabelHeight;
        long deltaX = candidate.X - preferredX;
        long deltaY = candidate.Y - preferredY;
        return deltaX * deltaX + deltaY * deltaY;
    }

    private static PixelRect Expand(PixelRect region, int amount) => new(
        region.X - amount,
        region.Y - amount,
        region.Width + amount * 2,
        region.Height + amount * 2);

    private static bool Intersects(PixelRect first, PixelRect second) =>
        first.X < second.Right &&
        first.Right > second.X &&
        first.Y < second.Bottom &&
        first.Bottom > second.Y;

    private static long OverlapArea(PixelRect first, PixelRect second)
    {
        int width = Math.Max(0, Math.Min(first.Right, second.Right) - Math.Max(first.X, second.X));
        int height = Math.Max(0, Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Y, second.Y));
        return (long)width * height;
    }

    private readonly record struct CandidateRoute(
        PixelRect Label,
        PlacementMarkerConnector Connector);
}
