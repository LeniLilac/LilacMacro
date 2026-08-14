namespace LilacMacro.App.Notifications;

public enum AppToastTone
{
    Error,
    Success,
}

public sealed record AppToast(string Title, string Message, AppToastTone Tone);

public static class AppToastService
{
    public static event EventHandler<AppToast>? Raised;

    public static void ShowError(string title, string message) => Raise(title, message, AppToastTone.Error);

    public static void ShowSuccess(string title, string message) => Raise(title, message, AppToastTone.Success);

    private static void Raise(string title, string message, AppToastTone tone)
    {
        string safeTitle = string.IsNullOrWhiteSpace(title)
            ? tone == AppToastTone.Success ? "COMPLETE" : "ERROR"
            : title.Trim();
        string safeMessage = string.IsNullOrWhiteSpace(message)
            ? tone == AppToastTone.Success ? "Completed." : "Unknown error."
            : message.Trim();
        Raised?.Invoke(null, new AppToast(safeTitle, safeMessage, tone));
    }
}
