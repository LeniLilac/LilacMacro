using System.ComponentModel;
using LilacMacro.Windows.Interop;

namespace LilacMacro.Windows;

public sealed class GlobalHotkeyRegistration : IDisposable
{
    public const int WindowMessage = 0x0312;
    public const uint F6VirtualKey = 0x75;
    private const uint NoRepeat = 0x4000;
    private readonly nint _window;
    private readonly int _id;
    private bool _disposed;

    public GlobalHotkeyRegistration(nint window, int id, uint virtualKey)
    {
        if (window == 0) throw new ArgumentException("A window handle is required.", nameof(window));
        _window = window;
        _id = id;
        if (!NativeMethods.RegisterHotKey(window, id, NoRepeat, virtualKey))
        {
            throw new Win32Exception("Could not register the global capture key.");
        }
    }

    public bool Matches(int message, nint parameter) => message == WindowMessage && parameter == _id;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ = NativeMethods.UnregisterHotKey(_window, _id);
    }
}
