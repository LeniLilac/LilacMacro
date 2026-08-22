namespace LilacMacro.Windows.Capture;

internal sealed class CaptureSurfaceChangedException(
    int expectedWidth,
    int expectedHeight,
    int actualWidth,
    int actualHeight,
    Exception? innerException = null)
    : Exception(
        $"Capture surface changed from {expectedWidth} × {expectedHeight} to {actualWidth} × {actualHeight}.",
        innerException)
{
    public int ExpectedWidth { get; } = expectedWidth;

    public int ExpectedHeight { get; } = expectedHeight;

    public int ActualWidth { get; } = actualWidth;

    public int ActualHeight { get; } = actualHeight;
}
