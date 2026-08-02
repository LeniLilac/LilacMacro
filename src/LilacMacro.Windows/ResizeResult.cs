using LilacMacro.Core.Geometry;

namespace LilacMacro.Windows;

public sealed record ResizeResult(
    PixelSize PreviousSize,
    PixelSize ActualSize,
    int Attempts,
    TimeSpan Elapsed);
