namespace LilacMacro.App.Infrastructure;

public sealed record AppSettings
{
    public int TargetWidth { get; init; } = 1280;

    public int TargetHeight { get; init; } = 720;

    public int FrameCount { get; init; } = 30;

    public double DurationSeconds { get; init; } = 10;

    public string DatasetRoot { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "LilacMacro Datasets");
}
