using LilacMacro.Core.Geometry;

namespace LilacMacro.Windows;

public sealed record RobloxWindowCandidateObservation(
    RobloxWindow Window,
    PixelSize? ClientSize,
    int InitialClientWidth,
    int InitialClientHeight,
    bool WasMinimized,
    string Outcome);

public sealed record RobloxWindowAcquisition(
    RobloxWindow? Window,
    ClientBounds? Bounds,
    IReadOnlyList<RobloxWindowCandidateObservation> Candidates)
{
    public bool Succeeded => Window is not null && Bounds is not null;
}
