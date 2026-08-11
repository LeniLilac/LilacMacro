using LilacMacro.App.Runtime;
using LilacMacro.App.Infrastructure;
using LilacMacro.Core.Automation;

namespace LilacMacro.Tests;

public sealed class MacroKeyBindingsTests
{
    [Fact]
    public void DefaultsProduceRuntimeSnapshot()
    {
        MacroRuntimeKeySnapshot snapshot = new MacroKeyBindings().Snapshot();

        Assert.Equal(0x76, snapshot.MacroToggle);
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

    [Fact]
    public void PersistedBindingsIgnoreInvalidEntriesWithoutDiscardingValidEntries()
    {
        MacroKeyBindings bindings = new();

        bindings.ApplyPersisted(new Dictionary<string, int?>
        {
            ["playmenu"] = null,
            [nameof(MacroKeyBindingId.QuickPlacement)] = -1,
            [nameof(MacroKeyBindingId.SellUnit)] = 'V',
            ["RemovedFeature"] = 'B',
        });

        Assert.Null(bindings.Snapshot().PlayMenu);
        Assert.Equal('Q', bindings.Snapshot().Placement.QuickPlacement);
        Assert.Equal('V', bindings.Snapshot().Placement.Sell);
    }

    [Fact]
    public void PersistedGlobalConflictFallsBackToSafeDefaults()
    {
        MacroKeyBindings bindings = new();

        bindings.ApplyPersisted(new Dictionary<string, int?>
        {
            [nameof(MacroKeyBindingId.MacroToggle)] = 'Q',
        });

        Assert.Equal(0x76, bindings.Snapshot().MacroToggle);
        Assert.Equal('Q', bindings.Snapshot().Placement.QuickPlacement);
    }

    [Fact]
    public async Task StoreRoundTripsBindingsAcrossOwnerStateRestart()
    {
        string root = Path.Combine(Path.GetTempPath(), "LilacMacro.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            MacroOwnerState first = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            first.KeyBindings[MacroKeyBindingId.MacroToggle].SetVirtualKey(0x77);
            first.KeyBindings[MacroKeyBindingId.PlayMenu].Unset();
            first.KeyBindings[MacroKeyBindingId.QuickPlacement].SetVirtualKey('R');
            await first.FlushAsync();

            MacroOwnerState second = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));

            Assert.Equal(0x77, second.KeyBindings.Snapshot().MacroToggle);
            Assert.Null(second.KeyBindings.Snapshot().PlayMenu);
            Assert.Equal('R', second.KeyBindings.Snapshot().Placement.QuickPlacement);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task LegacyF6MacroToggleMigratesToF7(int schemaVersion)
    {
        string root = Path.Combine(Path.GetTempPath(), "LilacMacro.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "macro-settings.json"),
                $$"""
                {
                  "schema_version": {{schemaVersion}},
                  "key_bindings": {
                    "MacroToggle": 117,
                    "PlayMenu": 76
                  },
                  "execution_target": 0
                }
                """);

            MacroSettings migrated = await new MacroSettingsStore(root).LoadAsync();

            Assert.Equal(MacroSettings.CurrentSchemaVersion, migrated.SchemaVersion);
            Assert.Equal(0x76, migrated.KeyBindings[nameof(MacroKeyBindingId.MacroToggle)]);
            Assert.Equal('L', migrated.KeyBindings[nameof(MacroKeyBindingId.PlayMenu)]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CurrentSchemaPreservesAnExplicitF6MacroToggle()
    {
        string root = Path.Combine(Path.GetTempPath(), "LilacMacro.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            await new MacroSettingsStore(root).SaveAsync(new MacroSettings
            {
                KeyBindings = new Dictionary<string, int?>
                {
                    [nameof(MacroKeyBindingId.MacroToggle)] = 0x75,
                },
            });

            MacroSettings settings = await new MacroSettingsStore(root).LoadAsync();

            Assert.Equal(MacroSettings.CurrentSchemaVersion, settings.SchemaVersion);
            Assert.Equal(0x75, settings.KeyBindings[nameof(MacroKeyBindingId.MacroToggle)]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
