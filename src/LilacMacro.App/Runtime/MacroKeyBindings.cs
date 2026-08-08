using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LilacMacro.Core.Automation;

namespace LilacMacro.App.Runtime;

internal enum MacroKeyBindingId
{
    MacroToggle,
    PlayMenu,
    UnitInventory,
    AreasMenu,
    CancelPlacement,
    QuickPlacement,
    ChangeTargeting,
    ChangeAutoUpgrade,
    UpgradeUnit,
    SellUnit,
    ShiftLock,
}

internal sealed class MacroKeyBindings
{
    private readonly Dictionary<MacroKeyBindingId, MacroKeyBinding> _byId;

    public MacroKeyBindings()
    {
        Items = new ObservableCollection<MacroKeyBinding>
        {
            Create(MacroKeyBindingId.MacroToggle, "Macro start / stop", "Global", 0x75),
            Create(MacroKeyBindingId.PlayMenu, "Play menu", "Navigation", 'P', canUnset: true),
            Create(MacroKeyBindingId.UnitInventory, "Unit inventory", "Team swap", 'U', canUnset: true),
            Create(MacroKeyBindingId.AreasMenu, "Areas menu", "Utilities", 'A', canUnset: true),
            Create(MacroKeyBindingId.CancelPlacement, "Cancel placement", "Step Mode", 'Z'),
            Create(MacroKeyBindingId.QuickPlacement, "Quick placement", "Step Mode", 'Q'),
            Create(MacroKeyBindingId.ChangeTargeting, "Change targeting", "Step Mode", 'T'),
            Create(MacroKeyBindingId.ChangeAutoUpgrade, "Change Auto Upgrade", "Step Mode", 'K'),
            Create(MacroKeyBindingId.UpgradeUnit, "Upgrade unit", "Step Mode", 'E'),
            Create(MacroKeyBindingId.SellUnit, "Sell unit", "Step Mode", 'X'),
            Create(MacroKeyBindingId.ShiftLock, "Shift lock", "Camera", KeyboardKey.LeftControl),
        };
        _byId = Items.ToDictionary(binding => binding.Id);
        foreach (MacroKeyBinding binding in Items) binding.PropertyChanged += Binding_OnPropertyChanged;
    }

    public event EventHandler? Changed;

    public ObservableCollection<MacroKeyBinding> Items { get; }

    public MacroKeyBinding this[MacroKeyBindingId id] => _byId[id];

    public MacroRuntimeKeySnapshot Snapshot() => new(
        Required(MacroKeyBindingId.MacroToggle),
        this[MacroKeyBindingId.PlayMenu].VirtualKey,
        this[MacroKeyBindingId.UnitInventory].VirtualKey,
        this[MacroKeyBindingId.AreasMenu].VirtualKey,
        new PlacementRuntimeKeys(
            Required(MacroKeyBindingId.QuickPlacement),
            Required(MacroKeyBindingId.CancelPlacement),
            Required(MacroKeyBindingId.ChangeTargeting),
            Required(MacroKeyBindingId.ChangeAutoUpgrade),
            Required(MacroKeyBindingId.UpgradeUnit),
            Required(MacroKeyBindingId.SellUnit),
            Required(MacroKeyBindingId.MacroToggle)),
        Required(MacroKeyBindingId.ShiftLock));

    public void Reset()
    {
        foreach (MacroKeyBinding binding in Items) binding.Reset();
    }

    private int Required(MacroKeyBindingId id) => this[id].VirtualKey
        ?? throw new InvalidOperationException($"{this[id].Name} must have a key.");

    private static MacroKeyBinding Create(
        MacroKeyBindingId id,
        string name,
        string scope,
        int defaultVirtualKey,
        bool canUnset = false) => new(id, name, scope, defaultVirtualKey, canUnset);

    private void Binding_OnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(MacroKeyBinding.VirtualKey)) Changed?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class MacroKeyBinding : INotifyPropertyChanged
{
    private readonly int _defaultVirtualKey;
    private int? _virtualKey;
    private bool _capturing;

    public MacroKeyBinding(
        MacroKeyBindingId id,
        string name,
        string scope,
        int defaultVirtualKey,
        bool canUnset)
    {
        if (!KeyboardKey.IsSupportedAutomationKey(defaultVirtualKey))
            throw new ArgumentOutOfRangeException(nameof(defaultVirtualKey));
        Id = id;
        Name = name;
        Scope = scope;
        CanUnset = canUnset;
        _defaultVirtualKey = defaultVirtualKey;
        _virtualKey = defaultVirtualKey;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MacroKeyBindingId Id { get; }

    public string Name { get; }

    public string Scope { get; }

    public bool CanUnset { get; }

    public int? VirtualKey
    {
        get => _virtualKey;
        private set
        {
            if (_virtualKey == value) return;
            _virtualKey = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(KeyName));
        }
    }

    public string KeyName => _capturing
        ? "PRESS KEY"
        : VirtualKey is int virtualKey
            ? KeyboardKey.GetDisplayName(virtualKey).ToUpperInvariant()
            : "NOT SET";

    public void SetVirtualKey(int virtualKey)
    {
        if (!KeyboardKey.IsSupportedAutomationKey(virtualKey))
            throw new InvalidDataException("Choose a supported key.");
        VirtualKey = virtualKey;
        SetCapturing(false);
    }

    public void Unset()
    {
        if (!CanUnset) throw new InvalidOperationException($"{Name} cannot be unset.");
        VirtualKey = null;
        SetCapturing(false);
    }

    public void Reset()
    {
        VirtualKey = _defaultVirtualKey;
        SetCapturing(false);
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

internal sealed record MacroRuntimeKeySnapshot(
    int MacroToggle,
    int? PlayMenu,
    int? UnitInventory,
    int? AreasMenu,
    PlacementRuntimeKeys Placement,
    int ShiftLock);
