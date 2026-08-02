using LilacMacro.Core.Geometry;

namespace LilacMacro.Windows.Capture;

public sealed record CapturedPng(PixelSize Size, byte[] Bytes);
