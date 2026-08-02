namespace LilacMacro.Windows;

public readonly record struct RobloxWindow(
    nint Handle,
    string Title,
    int ProcessId,
    string ProcessName);
