using LilacMacro.Core.Geometry;
using LilacMacro.Core.Placements;

namespace LilacMacro.App.Views;

public sealed record PlacementMarkerPresentation
{
    private const int PointRadius = 7;
    private const int StrokePadding = 2;

    public static PlacementMarkerPresentation Empty { get; } = new();

    public double CanvasLeft { get; init; }

    public double CanvasTop { get; init; }

    public double CanvasWidth { get; init; }

    public double CanvasHeight { get; init; }

    public double PointLeft { get; init; }

    public double PointTop { get; init; }

    public double AnchorX { get; init; }

    public double AnchorY { get; init; }

    public double LabelLeft { get; init; }

    public double LabelTop { get; init; }

    public double LabelWidth { get; init; }

    public double LabelHeight { get; init; }

    public double ConnectorBendX { get; init; }

    public double ConnectorBendY { get; init; }

    public double ConnectorEndX { get; init; }

    public double ConnectorEndY { get; init; }

    public static PlacementMarkerPresentation Create(
        int anchorX,
        int anchorY,
        PlacementMarkerLabelPlacement placement)
    {
        PixelRect label = placement.LabelBounds;
        PlacementMarkerConnector connector = placement.Connector;
        int canvasLeft = Math.Min(anchorX - PointRadius, label.X) - StrokePadding;
        int canvasTop = Math.Min(anchorY - PointRadius, label.Y) - StrokePadding;
        int canvasRight = Math.Max(anchorX + PointRadius, label.Right) + StrokePadding;
        int canvasBottom = Math.Max(anchorY + PointRadius, label.Bottom) + StrokePadding;

        return new PlacementMarkerPresentation
        {
            CanvasLeft = canvasLeft,
            CanvasTop = canvasTop,
            CanvasWidth = canvasRight - canvasLeft,
            CanvasHeight = canvasBottom - canvasTop,
            PointLeft = anchorX - PointRadius - canvasLeft,
            PointTop = anchorY - PointRadius - canvasTop,
            AnchorX = anchorX - canvasLeft,
            AnchorY = anchorY - canvasTop,
            LabelLeft = label.X - canvasLeft,
            LabelTop = label.Y - canvasTop,
            LabelWidth = label.Width,
            LabelHeight = label.Height,
            ConnectorBendX = connector.BendX - canvasLeft,
            ConnectorBendY = connector.BendY - canvasTop,
            ConnectorEndX = connector.EndX - canvasLeft,
            ConnectorEndY = connector.EndY - canvasTop,
        };
    }
}
