using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LilacMacro.App.Debugging;
using LilacMacro.Core.Automation;
using LilacMacro.Windows;

namespace LilacMacro.App.Views;

public partial class DebugKeyChainControl : UserControl
{
    private readonly ObservableCollection<DebugKeyChainEntry> _entries = [new()];
    private DebugKeySequenceCoordinator? _coordinator;
    private DebugKeyChainEntry? _capturingEntry;
    private bool _hostBusy;

    public DebugKeyChainControl()
    {
        InitializeComponent();
        KeyRows.ItemsSource = _entries;
    }

    internal void Initialize(DebugKeySequenceCoordinator coordinator)
    {
        if (_coordinator is not null) return;
        _coordinator = coordinator;
        _coordinator.Changed += Coordinator_OnChanged;
        UpdateState();
    }

    internal void SetHostBusy(bool busy)
    {
        _hostBusy = busy;
        UpdateState();
    }

    private void Add_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_coordinator?.State != DebugKeySequenceState.Idle ||
            _entries.Count >= AutomationKeySequence.MaximumSteps) return;
        FinishKeyCapture();
        _entries.Add(new DebugKeyChainEntry());
        UpdateState();
    }

    private void Remove_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_coordinator?.State != DebugKeySequenceState.Idle ||
            sender is not Button { Tag: DebugKeyChainEntry entry }) return;
        if (ReferenceEquals(_capturingEntry, entry)) FinishKeyCapture();
        _entries.Remove(entry);
        UpdateState();
    }

    private void Key_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_coordinator?.State != DebugKeySequenceState.Idle ||
            sender is not Button { Tag: DebugKeyChainEntry entry } button) return;
        FinishKeyCapture();
        _capturingEntry = entry;
        entry.SetCapturing(true);
        ShowStatus("PRESS KEY", "YellowBrush");
        Keyboard.Focus(button);
    }

    private void Control_OnPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (_capturingEntry is null) return;
        eventArgs.Handled = true;
        Key key = eventArgs.Key == Key.System ? eventArgs.SystemKey : eventArgs.Key;
        if (key == Key.Escape)
        {
            FinishKeyCapture();
            UpdateState();
            return;
        }

        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        try
        {
            _ = AutomationKeyPress.Create(
                virtualKey,
                AutomationKeyPress.DefaultHoldMilliseconds,
                checked((int)GlobalHotkeyRegistration.F6VirtualKey));
            _capturingEntry.SetVirtualKey(virtualKey);
            _capturingEntry = null;
            UpdateState();
        }
        catch (Exception error)
        {
            ShowStatus(error.Message, "DangerBrush");
        }
    }

    private async void Arm_OnClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_coordinator is null) return;
        if (_coordinator.State == DebugKeySequenceState.Armed)
        {
            _coordinator.Disarm();
            return;
        }
        if (_coordinator.State == DebugKeySequenceState.Running)
        {
            _coordinator.RequestStop();
            return;
        }
        if (_coordinator.State != DebugKeySequenceState.Idle) return;

        try
        {
            FinishKeyCapture();
            await _coordinator.ArmAsync(ReadSequence());
        }
        catch (Exception error)
        {
            ShowStatus(error.Message, "DangerBrush");
        }
    }

    private AutomationKeySequence ReadSequence()
    {
        List<AutomationKeyPress> steps = [];
        foreach (DebugKeyChainEntry entry in _entries)
        {
            if (!int.TryParse(
                    entry.HoldMillisecondsText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int holdMilliseconds))
            {
                throw new InvalidDataException("Enter hold time in milliseconds.");
            }
            steps.Add(AutomationKeyPress.Create(
                entry.VirtualKey,
                holdMilliseconds,
                checked((int)GlobalHotkeyRegistration.F6VirtualKey)));
        }
        return AutomationKeySequence.Create(steps);
    }

    private void FinishKeyCapture()
    {
        _capturingEntry?.SetCapturing(false);
        _capturingEntry = null;
    }

    private void Coordinator_OnChanged(object? sender, EventArgs eventArgs)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(UpdateState);
            return;
        }
        UpdateState();
    }

    private void UpdateState()
    {
        if (_coordinator is null) return;
        bool idle = _coordinator.State == DebugKeySequenceState.Idle;
        bool editable = idle && !_hostBusy;
        KeyRows.IsEnabled = editable;
        AddButton.IsEnabled = editable && _entries.Count < AutomationKeySequence.MaximumSteps;
        ArmButton.IsEnabled = !_hostBusy && _coordinator.State is not (
            DebugKeySequenceState.Arming or DebugKeySequenceState.Stopping);
        ArmButton.Content = _coordinator.State switch
        {
            DebugKeySequenceState.Armed => "DISARM",
            DebugKeySequenceState.Running => "CANCEL",
            _ => "ARM + FOCUS",
        };
        ArmButton.Style = (Style)FindResource(
            _coordinator.State == DebugKeySequenceState.Running
                ? "DangerButtonStyle"
                : "PrimaryButtonStyle");
        string brush = _coordinator.Status.StartsWith("ERROR", StringComparison.Ordinal)
            ? "DangerBrush"
            : _coordinator.State switch
            {
                DebugKeySequenceState.Armed or DebugKeySequenceState.Arming => "YellowBrush",
                DebugKeySequenceState.Running => "AccentBrush",
                _ when _coordinator.Status == "COMPLETE" => "SuccessBrush",
                _ => "MutedBrush",
            };
        ShowStatus(_coordinator.Status, brush);
    }

    private void ShowStatus(string status, string brush)
    {
        StatusText.Text = status;
        StatusText.ToolTip = status;
        StatusBorder.SetResourceReference(Border.BackgroundProperty, brush);
    }
}
