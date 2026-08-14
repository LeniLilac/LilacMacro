using LilacMacro.Core.Automation;

namespace LilacMacro.Tests;

public sealed class UtilityRespawnPolicyTests
{
    [Fact]
    public void CleanupOpensAreasBeforeRespawnKeys()
    {
        int[] keys = UtilityRespawnPolicy.CreateKeyOrder('U').ToArray();

        Assert.Equal(['U', KeyboardKey.Escape, 'R', KeyboardKey.Enter], keys);
    }

    [Fact]
    public void CleanupRejectsUnsupportedAreasKey()
    {
        Assert.Throws<InvalidDataException>(() => UtilityRespawnPolicy.CreateKeyOrder(0));
    }
}
