namespace LilacMacro.App.Infrastructure;

internal enum DiscordEventKind
{
    RunStarted,
    RunStopped,
    TaskChanged,
    Victory,
    Defeat,
    Recovery,
    TerminalFailure,
}

internal sealed record DiscordEventNotification(
    DiscordEventKind Kind,
    string Plan,
    string? Task,
    string Detail,
    string Instance,
    DateTimeOffset OccurredAtUtc,
    string? MentionUserId = null,
    byte[]? ScreenshotPng = null,
    bool ScreenshotCaptureAttempted = false);
