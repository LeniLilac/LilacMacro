namespace LilacMacro.Windows.Capture;

public sealed class RobloxCaptureUnavailableException : InvalidOperationException
{
    public RobloxCaptureUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
