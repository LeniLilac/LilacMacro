using System.ComponentModel;
using System.Runtime.CompilerServices;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Views;

internal sealed class DebugKeyChainEntry : INotifyPropertyChanged
{
    private int _virtualKey = AutomationKeyPress.DefaultVirtualKey;
    private string _holdMillisecondsText = AutomationKeyPress.DefaultHoldMilliseconds.ToString();
    private bool _capturing;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int VirtualKey => _virtualKey;

    public string KeyName => _capturing ? "PRESS..." : KeyboardKey.GetDisplayName(_virtualKey).ToUpperInvariant();

    public string HoldMillisecondsText
    {
        get => _holdMillisecondsText;
        set
        {
            if (_holdMillisecondsText == value) return;
            _holdMillisecondsText = value;
            OnPropertyChanged();
        }
    }

    public void SetVirtualKey(int virtualKey)
    {
        _virtualKey = virtualKey;
        _capturing = false;
        OnPropertyChanged(nameof(KeyName));
    }

    public void SetCapturing(bool capturing)
    {
        if (_capturing == capturing) return;
        _capturing = capturing;
        OnPropertyChanged(nameof(KeyName));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
