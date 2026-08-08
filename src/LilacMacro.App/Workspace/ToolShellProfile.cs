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
    IReadOnlyList<PageKind> Pages)
{
    public bool Includes(PageKind page) => Pages.Contains(page);

    public static ToolShellProfile Create(ToolShellKind kind) => kind switch
    {
        ToolShellKind.DatasetBuilder => new ToolShellProfile(
            kind,
            "Dataset Builder",
            "LilacMacro Dataset Builder",
            PageKind.Capture,
            [PageKind.Capture, PageKind.Review, PageKind.Datasets]),
        ToolShellKind.RuntimeLab => new ToolShellProfile(
            kind,
            "Runtime Lab",
            "LilacMacro Runtime Lab",
            PageKind.Debug,
            [PageKind.Debug, PageKind.WireTest]),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
