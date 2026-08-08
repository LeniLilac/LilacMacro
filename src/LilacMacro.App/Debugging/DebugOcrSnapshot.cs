using LilacMacro.App.Infrastructure;
using LilacMacro.Core.Automation;
using LilacMacro.Core.Datasets;
using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Debugging;

internal sealed record DebugOcrSnapshot(
    string State,
    string Source,
    PixelRect RegionOfInterest,
    OcrWorkerResult Ocr,
    IReadOnlyList<OcrTextRegion> Regions,
    OcrStateEvaluation Evaluation,
    IReadOnlyList<DebugVisualAnchorIntent> VisualAnchors);

internal sealed record DebugVisualAnchorIntent(
    string Text,
    OcrMatchMode MatchMode,
    OcrSpatialSelector SpatialSelector,
    string? SpatialAnchorText);

internal sealed record DebugRunReport(
    DebugOcrSnapshot Snapshot,
    bool Succeeded,
    string Status,
    IReadOnlyList<string> Events);

internal sealed record DebugStateTransitionObservation(
    ObservedStateTransitionOutcome Outcome,
    DebugOcrSnapshot Destination,
    DebugOcrSnapshot? Source)
{
    public DebugOcrSnapshot Result => Source ?? Destination;
}
