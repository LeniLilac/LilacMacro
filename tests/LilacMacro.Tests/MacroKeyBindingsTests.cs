using LilacMacro.App.Runtime;
using LilacMacro.Core.Automation;

namespace LilacMacro.Tests;

public sealed class MacroKeyBindingsTests
{
    [Fact]
    public void DefaultsProduceRuntimeSnapshot()
    {
        MacroRuntimeKeySnapshot snapshot = new MacroKeyBindings().Snapshot();

        Assert.Equal(0x75, snapshot.MacroToggle);
        Assert.Equal('P', snapshot.PlayMenu);
        Assert.Equal('U', snapshot.UnitInventory);
        Assert.Equal('A', snapshot.AreasMenu);
        Assert.Equal('Q', snapshot.Placement.QuickPlacement);
        Assert.Equal('Z', snapshot.Placement.CancelPlacement);
        Assert.Equal(KeyboardKey.LeftControl, snapshot.ShiftLock);
    }

    [Fact]
    public void OptionalNavigationKeyCanBeUnsetAndReset()
    {
        MacroKeyBindings bindings = new();
        MacroKeyBinding play = bindings[MacroKeyBindingId.PlayMenu];

        play.Unset();
        Assert.Null(bindings.Snapshot().PlayMenu);
        Assert.Equal("NOT SET", play.KeyName);

        bindings.Reset();
        Assert.Equal('P', bindings.Snapshot().PlayMenu);
    }

    [Fact]
    public void RequiredBindingRejectsUnset()
    {
        MacroKeyBinding binding = new MacroKeyBindings()[MacroKeyBindingId.QuickPlacement];

        Assert.Throws<InvalidOperationException>(binding.Unset);
    }
}
