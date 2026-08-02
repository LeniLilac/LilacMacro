namespace LilacMacro.App.Workspace;

public sealed record CaptureProgress(int Completed, int Total, string Message)
{
    public double Percent => Total == 0 ? 0 : Completed * 100d / Total;
}
