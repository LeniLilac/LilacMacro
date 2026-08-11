using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LilacMacro.Windows.LocalSession;

public static class RunnerDesktopPersonalization
{
    private const uint SpiSetDesktopWallpaper = 0x0014;
    private const uint UpdateIniFile = 0x01;
    private const uint SendChange = 0x02;
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;

    public static void ApplyCurrentSession()
    {
        SetString(@"Control Panel\Desktop", "Wallpaper", string.Empty);
        SetString(@"Control Panel\Desktop", "WallpaperStyle", "0");
        SetString(@"Control Panel\Desktop", "TileWallpaper", "0");
        SetString(@"Control Panel\Colors", "Background", "0 0 0");
        SetDword(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideIcons", 1);
        if (!SystemParametersInfo(SpiSetDesktopWallpaper, 0, string.Empty, UpdateIniFile | SendChange))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "The runner desktop background could not be applied.");
        nint setting = Marshal.StringToHGlobalUni(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
        try
        {
            _ = SendMessageTimeout(new nint(0xFFFF), WmSettingChange, nint.Zero, setting, SmtoAbortIfHung, 2000, out _);
        }
        finally { Marshal.FreeHGlobal(setting); }
    }

    private static void SetString(string keyPath, string name, string value)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true)
            ?? throw new InvalidOperationException("The runner desktop registry key could not be opened.");
        key.SetValue(name, value, RegistryValueKind.String);
    }

    private static void SetDword(string keyPath, string name, int value)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true)
            ?? throw new InvalidOperationException("The runner desktop registry key could not be opened.");
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, string value, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessageTimeout(
        nint window,
        uint message,
        nint parameter,
        nint data,
        uint flags,
        uint timeout,
        out nint result);
}
