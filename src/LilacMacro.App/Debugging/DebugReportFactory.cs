using LilacMacro.Core.Geometry;
using LilacMacro.Core.Ocr;

namespace LilacMacro.App.Debugging;

internal static class DebugReportFactory
{
    public static DebugRunReport StateReport(DebugOcrSnapshot snapshot) => new(
        snapshot,
        snapshot.Evaluation.IsMatch,
        snapshot.Evaluation.IsMatch ? $"{snapshot.State} TRUE" : $"{snapshot.State} FALSE",
        [StateLine(snapshot)]);

    public static DebugRunReport FailedState(DebugOcrSnapshot snapshot) => new(
        snapshot,
        false,
        $"{snapshot.State} FALSE",
        [StateLine(snapshot), "INPUT BLOCKED"]);

    public static DebugRunReport MissingTarget(DebugOcrSnapshot snapshot, string target) => new(
        snapshot,
        false,
        $"{target} NOT FOUND",
        [StateLine(snapshot), "INPUT BLOCKED"]);

    public static DebugRunReport MissingActAnchors(
        DebugOcrSnapshot snapshot,
        ActPickerKind kind) => new(
        snapshot,
        false,
        "ACT ANCHORS MISSING",
        [
            StateLine(snapshot),
            $"EXACT {kind.ToString().ToUpperInvariant()} + SELECT STAGE REQUIRED",
            "INPUT BLOCKED",
        ]);

    public static DebugRunReport UnsupportedAct(
        DebugOcrSnapshot snapshot,
        StoryAct act) => new(
        snapshot,
        false,
        $"{act.ToString().ToUpperInvariant()} NOT AVAILABLE",
        [StateLine(snapshot), "INPUT BLOCKED"]);

    public static DebugRunReport MissingChallengeAnchors(DebugOcrSnapshot snapshot) => new(
        snapshot,
        false,
        "CHALLENGE ANCHORS MISSING",
        [StateLine(snapshot), "EXACT CHALLENGE + DAILY + WEEKLY REQUIRED", "INPUT BLOCKED"]);

    public static DebugRunReport ActClickBlocked(
        DebugOcrSnapshot confirmation,
        string status,
        DebugOcrSnapshot initial,
        ActPickerLayout layout,
        string act,
        PixelPoint point) => new(
        confirmation,
        false,
        status,
        [
            StateLine(initial),
            LayoutLine(layout),
            $"{act.ToUpperInvariant()} [{point.X},{point.Y}] DERIVED",
            "WAIT 250 MS",
            StateLine(confirmation),
            "SELECT STAGE BLOCKED",
        ]);

    public static DebugRunReport ClickReport(
        DebugOcrSnapshot snapshot,
        OcrTargetMatch target,
        PixelPoint point,
        string anchor) => new(
        snapshot,
        true,
        $"{target.Target.ToUpperInvariant()} CLICKED",
        [StateLine(snapshot), $"{target.Target.ToUpperInvariant()} [{point.X},{point.Y}] {anchor}"]);

    public static DebugRunReport DerivedClickReport(
        DebugOcrSnapshot snapshot,
        string target,
        PixelPoint point,
        string layout) => new(
        snapshot,
        true,
        $"{target} CLICKED",
        [StateLine(snapshot), layout, $"{target} [{point.X},{point.Y}] DERIVED"]);

    public static string StateLine(DebugOcrSnapshot snapshot) =>
        $"{snapshot.State} {snapshot.Evaluation.Matches.Count}/{snapshot.Evaluation.RequiredMatches} " +
        $"{snapshot.Ocr.InferenceMilliseconds} MS {snapshot.Ocr.Device.ToUpperInvariant()}";

    public static string LayoutLine(ActPickerLayout layout) =>
        $"{layout.Kind.ToString().ToUpperInvariant()} " +
        $"[{layout.ModeBounds.X},{layout.ModeBounds.Y},{layout.ModeBounds.Width},{layout.ModeBounds.Height}] " +
        $"SELECT STAGE [{layout.SelectStageBounds.X},{layout.SelectStageBounds.Y}," +
        $"{layout.SelectStageBounds.Width},{layout.SelectStageBounds.Height}] SPAN {layout.VerticalSpan}";

    public static string ChallengeLayoutLine(ChallengeTypePickerLayout layout) =>
        $"CHALLENGE [{layout.ChallengeBounds.X},{layout.ChallengeBounds.Y}," +
        $"{layout.ChallengeBounds.Width},{layout.ChallengeBounds.Height}] DAILY " +
        $"[{layout.DailyBounds.X},{layout.DailyBounds.Y},{layout.DailyBounds.Width},{layout.DailyBounds.Height}] " +
        $"WEEKLY [{layout.WeeklyBounds.X},{layout.WeeklyBounds.Y}," +
        $"{layout.WeeklyBounds.Width},{layout.WeeklyBounds.Height}] SCALE {layout.Scale}";
}
