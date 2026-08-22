using System.Windows.Interop;
using System.ComponentModel;
using LilacMacro.App.Notifications;
using LilacMacro.App.Runtime;
using LilacMacro.App.Workspace;
using LilacMacro.Windows;

namespace LilacMacro.App;

public partial class MacroShellWindow
{
    private const int MacroToggleHotkeyId = 0x4C50;
    private HwndSource? _macroHotkeySource;
    private GlobalHotkeyRegistration? _macroHotkey;
    private bool _macroHotkeyCaptureSuspended;

    private void InitializeMacroHotkey()
    {
        SourceInitialized += MacroShellWindow_OnSourceInitialized;
        _ownerState.KeyBindings.Changed += KeyBindings_OnChanged;
    }

    private void MacroShellWindow_OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        nint handle = new WindowInteropHelper(this).Handle;
        _macroHotkeySource = HwndSource.FromHwnd(handle)
            ?? throw new InvalidOperationException("Could not attach the macro key to LilacMacro.");
        _macroHotkeySource.AddHook(MacroHotkeyMessageHook);
        RegisterMacroHotkey();
    }

    private void KeyBindings_OnChanged(object? sender, EventArgs eventArgs)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(RegisterMacroHotkey);
            return;
        }
        RegisterMacroHotkey();
    }

    private void RegisterMacroHotkey()
    {
        _macroHotkey?.Dispose();
        _macroHotkey = null;
        if (_macroHotkeyCaptureSuspended || _macroHotkeySource is null) return;

        try
        {
            int virtualKey = _ownerState.KeyBindings[MacroKeyBindingId.MacroToggle].VirtualKey
                ?? throw new InvalidOperationException("Macro start / stop must have a key.");
            _macroHotkey = new GlobalHotkeyRegistration(
                _macroHotkeySource.Handle,
                MacroToggleHotkeyId,
                checked((uint)virtualKey));
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or Win32Exception)
        {
            string keyName = _ownerState.KeyBindings[MacroKeyBindingId.MacroToggle].KeyName;
            AppToastService.ShowError(
                "KEYBIND UNAVAILABLE",
                $"Macro start / stop ({keyName}) is already in use. Choose another key or close the conflicting app.");
        }
    }

    private nint MacroHotkeyMessageHook(
        nint window,
        int message,
        nint parameter,
        nint data,
        ref bool handled)
    {
        if (_macroHotkey?.Matches(message, parameter) != true) return 0;
        handled = true;
        MacroHotkeyTarget target = MacroHotkeyRoutingPolicy.Resolve(
            _setupPage.IsTestRunning,
            _macroPage.IsRunning,
            _currentPage == MacroShellPage.Macro);
        if (target == MacroHotkeyTarget.SetupTest)
        {
            _setupPage.TryStopTest();
        }
        else if (target == MacroHotkeyTarget.Macro)
        {
            _macroPage.ToggleRunFromHotkey();
        }
        return 0;
    }

    private void SetMacroHotkeyCaptureSuspended(bool suspended)
    {
        _macroHotkeyCaptureSuspended = suspended;
        RegisterMacroHotkey();
    }

    private void DisposeMacroHotkey()
    {
        _ownerState.KeyBindings.Changed -= KeyBindings_OnChanged;
        _macroHotkey?.Dispose();
        _macroHotkey = null;
        if (_macroHotkeySource is not null)
        {
            _macroHotkeySource.RemoveHook(MacroHotkeyMessageHook);
            _macroHotkeySource = null;
        }
    }
}
