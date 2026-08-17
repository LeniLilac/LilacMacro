using LilacMacro.Core.Placements;

namespace LilacMacro.Tests;

public sealed class PlacementSelectionRetryPolicyTests
{
    [Fact]
    public void SelectionAttemptsAreBoundedToThree()
    {
        Assert.Equal(3, PlacementSelectionRetryPolicy.MaximumAttempts);
        Assert.True(PlacementSelectionRetryPolicy.ShouldRetry(1));
        Assert.True(PlacementSelectionRetryPolicy.ShouldRetry(2));
        Assert.False(PlacementSelectionRetryPolicy.ShouldRetry(3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void SelectionRetryPolicyRejectsAttemptsOutsideBound(int attempt) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PlacementSelectionRetryPolicy.ShouldRetry(attempt));
}
