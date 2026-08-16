using LilacMacro.Core.LocalSession;

namespace LilacMacro.Tests;

public sealed partial class LocalSessionContractTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public void Story_snapshot_requires_a_bounded_infinite_reset_wave(int wave)
    {
        RunnerRuntimeSnapshot snapshot = ValidSnapshot() with
        {
            Tasks = [ValidSnapshot().Tasks[0] with { Route = "East Town · Infinite", InfiniteWave = wave }],
        };

        LocalSessionValidationResult result = LocalSessionValidation.Validate(
            snapshot,
            "S-1-5-21-100",
            "1.0.30");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Infinite reset wave", StringComparison.Ordinal));
    }
}
