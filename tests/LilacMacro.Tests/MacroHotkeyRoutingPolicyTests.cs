using LilacMacro.App.Runtime;

namespace LilacMacro.Tests;

public sealed class MacroHotkeyRoutingPolicyTests
{
    [Theory]
    [InlineData(true, false, false, nameof(MacroHotkeyTarget.SetupTest))]
    [InlineData(true, true, true, nameof(MacroHotkeyTarget.SetupTest))]
    [InlineData(false, true, false, nameof(MacroHotkeyTarget.Macro))]
    [InlineData(false, true, true, nameof(MacroHotkeyTarget.Macro))]
    [InlineData(false, false, true, nameof(MacroHotkeyTarget.Macro))]
    [InlineData(false, false, false, nameof(MacroHotkeyTarget.None))]
    public void ResolvesOnlyValidStartAndStopTargets(
        bool setupTestRunning,
        bool macroRunning,
        bool macroPageActive,
        string expected) =>
        Assert.Equal(
            expected,
            MacroHotkeyRoutingPolicy.Resolve(setupTestRunning, macroRunning, macroPageActive).ToString());
}
