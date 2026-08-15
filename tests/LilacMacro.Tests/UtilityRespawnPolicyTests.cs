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

    [Fact]
    public void CleanupDoesNotAcceptLobbyBehindFadingAreasOverlay()
    {
        int stable = 0;
        stable = UtilityRespawnPolicy.UpdateSettledLobbyObservations(
            stable, lobbyObserved: true, areasObserved: true);
        Assert.Equal(0, stable);

        stable = UtilityRespawnPolicy.UpdateSettledLobbyObservations(
            stable, lobbyObserved: true, areasObserved: false);
        Assert.Equal(1, stable);

        stable = UtilityRespawnPolicy.UpdateSettledLobbyObservations(
            stable, lobbyObserved: true, areasObserved: false);
        Assert.Equal(UtilityRespawnPolicy.RequiredSettledLobbyObservations, stable);
    }

    [Fact]
    public void CleanupResetsSettledLobbyEvidenceWhenAreasReturns()
    {
        int stable = UtilityRespawnPolicy.UpdateSettledLobbyObservations(
            1, lobbyObserved: true, areasObserved: true);

        Assert.Equal(0, stable);
    }

    [Fact]
    public void CleanupClosesFreshPostRespawnAreasOverlay()
    {
        Assert.True(UtilityRespawnPolicy.ShouldCloseAreas(
            areasObserved: true,
            cleanupAttempts: 0,
            observationsSinceCleanup: UtilityRespawnPolicy.ObservationsBetweenAreasCleanupAttempts));
    }

    [Theory]
    [InlineData(false, 0, 6)]
    [InlineData(true, 1, 5)]
    [InlineData(true, 2, 6)]
    public void CleanupDoesNotSpamAreasKey(
        bool areasObserved,
        int cleanupAttempts,
        int observationsSinceCleanup)
    {
        Assert.False(UtilityRespawnPolicy.ShouldCloseAreas(
            areasObserved,
            cleanupAttempts,
            observationsSinceCleanup));
    }
}
