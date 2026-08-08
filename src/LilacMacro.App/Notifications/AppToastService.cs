namespace LilacMacro.App.Notifications;

public sealed record AppErrorToast(string Title, string Message);

public static class AppToastService
{
    public static event EventHandler<AppErrorToast>? ErrorRaised;

    public static void ShowError(string title, string message)
    {
        string safeTitle = string.IsNullOrWhiteSpace(title) ? "ERROR" : title.Trim();
        string safeMessage = string.IsNullOrWhiteSpace(message) ? "Unknown error." : message.Trim();
        ErrorRaised?.Invoke(null, new AppErrorToast(safeTitle, safeMessage));
    }
}
