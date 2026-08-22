using LilacMacro.Core.Geometry;

namespace LilacMacro.Windows.Capture;

public sealed record CapturedPng(
    PixelSize Size,
    byte[] Bytes,
    CaptureColorDiagnostics? ColorDiagnostics = null,
    CaptureFrameDiagnostics? FrameDiagnostics = null);

public sealed record CaptureFrameDiagnostics(
    double MeanSrgbLuminance,
    double P95SrgbLuminance,
    double NearWhitePixelPercent,
    double ClippedPixelPercent,
    double DarkPixelPercent);
