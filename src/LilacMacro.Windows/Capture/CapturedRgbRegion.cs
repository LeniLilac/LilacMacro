using LilacMacro.Core.Geometry;
using LilacMacro.Core.Imaging;

namespace LilacMacro.Windows.Capture;

public sealed record CapturedRgbRegion(PixelRect Region, RgbImage Image);
