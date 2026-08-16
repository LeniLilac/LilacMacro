using LilacMacro.App.Infrastructure;
using LilacMacro.App.Runtime;
using LilacMacro.App.Views;
using LilacMacro.Core.Security;

namespace LilacMacro.Tests;

public sealed class MacroPlanPersistenceTests
{
    [Fact]
    public async Task OwnerStateRoundTripsPlansOrderFieldsAndSelection()
    {
        string root = TemporaryRoot();
        try
        {
            MacroOwnerState first = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            PlanPrototype plan = first.Plans[0];
            plan.Name = "Saved rotation";
            plan.Blocks.Clear();
            plan.Blocks.Add(new PlanTaskPrototype
            {
                Priority = 1,
                Mode = PlanTaskMode.Expedition,
                Route = "Rose Kingdom",
                Target = 37,
                DefeatRetries = 4,
                Difficulty = 3,
                BossesBeforeExtract = 6,
                ExtractAtCheckpoint = false,
                RewardTarget = "Equipment Lock",
            });
            PlanLoopPrototype loop = new() { Label = "Night loop", Forever = false, RepeatCount = 9 };
            loop.Children.Add(new PlanTaskPrototype
            {
                Priority = 2,
                Mode = PlanTaskMode.Challenge,
                Route = "Challenge",
                RunTrait = true,
                RunStat = false,
                RunSprite = true,
            });
            plan.Blocks.Add(loop);
            plan.Blocks.Add(new PlanTaskPrototype
            {
                Priority = 3,
                Mode = PlanTaskMode.Utilities,
                Route = LilacMacro.Core.Automation.ShopPurchasePolicy.GoldRoute,
                Target = 1,
                ShopItemIds = ["trait-crystal", "equipment-lock"],
            });
            plan.Blocks.Add(new PlanTaskPrototype
            {
                Priority = 4,
                Mode = PlanTaskMode.Utilities,
                Route = LilacMacro.Core.Automation.ResourceRefuelPolicy.CombinedRoute,
                Target = 400,
            });
            plan.Blocks.Add(new PlanTaskPrototype
            {
                Priority = 5,
                Mode = PlanTaskMode.Story,
                Route = "East Town · Infinite",
                Target = 3,
                InfiniteWave = 321,
            });
            first.SelectPlan(first.Plans[1]);
            first.NotifyPlansChanged();
            await first.FlushAsync();

            MacroOwnerState second = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));

            Assert.Equal(1, second.SelectedPlanIndex);
            Assert.Same(second.Plans[1], second.SelectedPlan);
            PlanPrototype restored = second.Plans[0];
            Assert.Equal("Saved rotation", restored.Name);
            PlanTaskPrototype expedition = Assert.IsType<PlanTaskPrototype>(restored.Blocks[0]);
            Assert.Equal(1, expedition.Priority);
            Assert.Equal(PlanTaskMode.Expedition, expedition.Mode);
            Assert.Equal("Rose Kingdom", expedition.Route);
            Assert.Equal(37, expedition.Target);
            Assert.Equal(4, expedition.DefeatRetries);
            Assert.Equal(3, expedition.Difficulty);
            Assert.Equal(6, expedition.BossesBeforeExtract);
            Assert.False(expedition.ExtractAtCheckpoint);
            Assert.Equal("Equipment Lock", expedition.RewardTarget);
            PlanLoopPrototype restoredLoop = Assert.IsType<PlanLoopPrototype>(restored.Blocks[1]);
            Assert.Equal("Night loop", restoredLoop.Label);
            Assert.False(restoredLoop.Forever);
            Assert.Equal(9, restoredLoop.RepeatCount);
            PlanTaskPrototype challenge = Assert.IsType<PlanTaskPrototype>(Assert.Single(restoredLoop.Children));
            Assert.Equal(2, challenge.Priority);
            Assert.True(challenge.RunTrait);
            Assert.False(challenge.RunStat);
            Assert.True(challenge.RunSprite);
            PlanTaskPrototype shop = Assert.IsType<PlanTaskPrototype>(restored.Blocks[2]);
            Assert.Equal(["trait-crystal", "equipment-lock"], shop.ShopItemIds);
            PlanTaskPrototype combinedRefuel = Assert.IsType<PlanTaskPrototype>(restored.Blocks[3]);
            Assert.Equal(LilacMacro.Core.Automation.ResourceRefuelPolicy.CombinedRoute, combinedRefuel.Route);
            Assert.Equal(400, combinedRefuel.Target);
            PlanTaskPrototype infinite = Assert.IsType<PlanTaskPrototype>(restored.Blocks[4]);
            Assert.Equal("East Town · Infinite", infinite.Route);
            Assert.Equal(3, infinite.Target);
            Assert.Equal(321, infinite.InfiniteWave);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task InvalidPlanPayloadFallsBackWithoutDiscardingValidSettings()
    {
        string root = TemporaryRoot();
        try
        {
            await new MacroSettingsStore(root).SaveAsync(new MacroSettings
            {
                KeyBindings = new Dictionary<string, int?>
                {
                    [nameof(MacroKeyBindingId.MacroToggle)] = 0x77,
                },
                Plans =
                [
                    new PlanSettingsSnapshot
                    {
                        Name = "Invalid",
                        Blocks =
                        [
                            new PlanBlockSettingsSnapshot
                            {
                                Kind = "task",
                                Mode = nameof(PlanTaskMode.Story),
                                Route = string.Empty,
                                Target = 1,
                            },
                        ],
                    },
                ],
            });

            MacroOwnerState restored = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));

            Assert.Equal(0x77, restored.KeyBindings.Snapshot().MacroToggle);
            Assert.Equal("Daily rotation", restored.Plans[0].Name);
            Assert.NotEmpty(restored.Plans[0].Blocks);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task SchemaThreeSettingsMigrateToDefaultPlansAndSaveCurrentSchema()
    {
        string root = TemporaryRoot();
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "macro-settings.json"),
                """
                {
                  "schema_version": 3,
                  "key_bindings": { "MacroToggle": 119 },
                  "execution_target": 0
                }
                """);

            MacroOwnerState owner = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));
            Assert.Equal("Daily rotation", owner.Plans[0].Name);
            Assert.Equal(0x77, owner.KeyBindings.Snapshot().MacroToggle);

            owner.NotifyPlansChanged();
            await owner.FlushAsync();
            MacroSettings saved = await new MacroSettingsStore(root).LoadAsync();

            Assert.Equal(MacroSettings.CurrentSchemaVersion, saved.SchemaVersion);
            Assert.NotEmpty(saved.Plans);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public async Task ExistingSettingsDoNotSilentlyOptIntoNewDiscordEvents()
    {
        string root = TemporaryRoot();
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, "macro-settings.json"),
                """
                {
                  "schema_version": 11,
                  "notify_on_terminal_failure": true
                }
                """);

            MacroOwnerState owner = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root));

            Assert.True(owner.NotifyOnTerminalFailure);
            Assert.False(owner.NotifyOnRunStart);
            Assert.False(owner.NotifyOnRunStop);
            Assert.False(owner.NotifyOnTaskChange);
            Assert.False(owner.NotifyOnVictory);
            Assert.False(owner.NotifyOnDefeat);
            Assert.False(owner.NotifyOnRecovery);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void NullNestedPlanPayloadIsRejectedWithoutThrowing()
    {
        PlanSettingsSnapshot snapshot = new()
        {
            Name = "Broken",
            Blocks = null!,
        };

        bool restored = PlanPersistence.TryRestore([snapshot], out _);

        Assert.False(restored);
    }

    [Fact]
    public async Task OwnerStateRoundTripsProtectedReportingAndPrivateServerSettings()
    {
        string root = TemporaryRoot();
        FakeSecretProtector protector = new();
        try
        {
            MacroOwnerState first = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root), protector);
            first.SetPrivateServerLink("https://www.roblox.com/share?code=private-code&type=Server");
            string webhook = "https://discord.com" + "/api/webhooks/123/private-token";
            first.SetDiscordWebhook(webhook);
            first.SetDiscordEventOptions(
                "123456789012345678",
                notifyOnRunStart: false,
                notifyOnRunStop: true,
                notifyOnTaskChange: false,
                notifyOnVictory: true,
                notifyOnDefeat: false,
                notifyOnRecovery: true,
                notifyOnTerminalFailure: false);
            await first.FlushAsync();

            string json = await File.ReadAllTextAsync(Path.Combine(root, "macro-settings.json"));
            Assert.DoesNotContain("private-code", json, StringComparison.Ordinal);
            Assert.DoesNotContain("private-token", json, StringComparison.Ordinal);
            Assert.DoesNotContain("execution_target", json, StringComparison.Ordinal);

            MacroOwnerState second = await MacroOwnerState.LoadAsync(new MacroSettingsStore(root), protector);
            Assert.Equal("https://www.roblox.com/share?code=private-code&type=Server", second.PrivateServerLink);
            Assert.Equal(webhook, second.DiscordWebhook);
            Assert.Equal("123456789012345678", second.DiscordUserId);
            Assert.False(second.NotifyOnTerminalFailure);
            Assert.False(second.NotifyOnRunStart);
            Assert.True(second.NotifyOnRunStop);
            Assert.False(second.NotifyOnTaskChange);
            Assert.True(second.NotifyOnVictory);
            Assert.False(second.NotifyOnDefeat);
            Assert.True(second.NotifyOnRecovery);
        }
        finally
        {
            Delete(root);
        }
    }

    private static string TemporaryRoot() =>
        Path.Combine(Path.GetTempPath(), "LilacMacro.Tests", Guid.NewGuid().ToString("N"));

    private static void Delete(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class FakeSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext.Length == 0
            ? string.Empty
            : Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"protected:{plaintext}"));

        public string Unprotect(string protectedValue) => protectedValue.Length == 0
            ? string.Empty
            : System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue))["protected:".Length..];
    }
}
