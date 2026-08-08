using LilacMacro.Core.Geometry;
using LilacMacro.Core.Vision;

namespace LilacMacro.Windows.Capture;

public sealed record CapturedGrayRegion(PixelRect Region, GrayImage Image);
