using LilacMacro.App.Runtime;
using LilacMacro.App.Views;
using LilacMacro.Core.Placements;

namespace LilacMacro.Tests;

public sealed class PlacementStepEditorPolicyTests
{
    [Fact]
    public void ReferenceOptionsUseOnlyCompactPlacementLabels()
    {
        PlacementStep one = Placement(1);
        PlacementStep sixA = Placement(6);
        PlacementStep sixB = Placement(6);

        PlacementReferenceOption[] options = PlacementStepEditorDialog.BuildReferenceOptions(
            [one, sixA, sixB],
            [one, sixA, sixB]);

        Assert.Equal(["1", "6a", "6b"], options.Select(option => option.Label));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void MacroHotkeyPrioritizesTheActiveSetupTest(
        bool setupTestRunning,
        bool expectedSetupTest) =>
        Assert.Equal(
            expectedSetupTest ? MacroHotkeyTarget.SetupTest : MacroHotkeyTarget.Macro,
            MacroHotkeyRoutingPolicy.Resolve(setupTestRunning));

    private static PlacementStep Placement(int slot) => new()
    {
        Kind = PlacementStepKind.Place,
        UnitSlot = slot,
        X = slot * 10,
        Y = slot * 10,
    };
}
