using System.Runtime.InteropServices;

namespace LilacMacro.Windows;

public static class WindowsDesktopMetrics
{
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

    public static (int Width, int Height) PrimaryDisplaySize() =>
        (GetSystemMetrics(SmCxScreen), GetSystemMetrics(SmCyScreen));

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
