using LilacMacro.App.Infrastructure;

namespace LilacMacro.App.Workspace;

internal enum ToolShellKind
{
    DatasetBuilder,
    RuntimeLab,
}

internal sealed record ToolShellProfile(
    ToolShellKind Kind,
    string DisplayName,
    string WindowTitle,
    PageKind StartPage,
    IReadOnlyList<PageKind> Pages,
    string OcrDevice,
    bool KeepOcrLoaded,
    bool PreloadOcrOnOpen,
    bool RetainAllDeepDebugFrames)
{
    public bool Includes(PageKind page) => Pages.Contains(page);

    public static ToolShellProfile Create(ToolShellKind kind) => kind switch
    {
        ToolShellKind.DatasetBuilder => new ToolShellProfile(
            kind,
            "Dataset Builder",
            "LilacMacro Dataset Builder",
            PageKind.Capture,
            [PageKind.Capture, PageKind.Review, PageKind.Datasets],
            OcrRunner.GpuDevice,
            KeepOcrLoaded: true,
            PreloadOcrOnOpen: true,
            RetainAllDeepDebugFrames: false),
        ToolShellKind.RuntimeLab => new ToolShellProfile(
            kind,
            "Runtime Lab",
            "LilacMacro Runtime Lab",
            PageKind.Debug,
            [
                PageKind.Debug,
                PageKind.WireTest,
                PageKind.ScrollTest,
                PageKind.TeamSwapTest,
                PageKind.RouteOptimizerTest,
                PageKind.UtilityTaskTest,
            ],
            OcrRunner.GpuDevice,
            KeepOcrLoaded: true,
            PreloadOcrOnOpen: true,
            RetainAllDeepDebugFrames: true),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
