namespace LilacMacro.App.Views;

public sealed record PlacementMarkerPresentation
{
    public const double NearbyRadiusPixels = 72;
    private const int MarkerWidth = 66;
    private const int MarkerHeight = 46;
    private const int AnchorX = 24;
    private const int AnchorY = 36;

    public static PlacementMarkerPresentation Empty { get; } = new();

    public double CanvasLeft { get; init; }

    public double CanvasTop { get; init; }

    public double CanvasWidth { get; init; }

    public double CanvasHeight { get; init; }

    public static PlacementMarkerPresentation Create(int anchorX, int anchorY) => new()
    {
        CanvasLeft = anchorX - AnchorX,
        CanvasTop = anchorY - AnchorY,
        CanvasWidth = MarkerWidth,
        CanvasHeight = MarkerHeight,
    };

    public static bool IsNearPointer(
        double anchorX,
        double anchorY,
        double pointerX,
        double pointerY,
        double zoom)
    {
        double safeZoom = Math.Max(0.01, zoom);
        double logicalRadius = NearbyRadiusPixels / safeZoom;
        double deltaX = anchorX - pointerX;
        double deltaY = anchorY - pointerY;
        return deltaX * deltaX + deltaY * deltaY <= logicalRadius * logicalRadius;
    }
}
