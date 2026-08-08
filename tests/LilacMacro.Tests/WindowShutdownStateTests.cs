using LilacMacro.App.Lifecycle;

namespace LilacMacro.Tests;

public sealed class WindowShutdownStateTests
{
    [Fact]
    public void BeginClose_FirstRequestRequiresFlush()
    {
        WindowShutdownState state = new();

        WindowShutdownDecision decision = state.BeginClose();

        Assert.Equal(WindowShutdownDecision.CancelAndFlush, decision);
    }

    [Fact]
    public void BeginClose_WhileFlushRunsCancelsWithoutStartingAnotherFlush()
    {
        WindowShutdownState state = new();
        _ = state.BeginClose();

        WindowShutdownDecision decision = state.BeginClose();

        Assert.Equal(WindowShutdownDecision.CancelWhileFlushing, decision);
    }

    [Fact]
    public void BeginClose_AfterSuccessfulFlushAllowsQueuedClose()
    {
        WindowShutdownState state = new();
        _ = state.BeginClose();
        state.CompleteFlush();

        WindowShutdownDecision decision = state.BeginClose();

        Assert.Equal(WindowShutdownDecision.AllowClose, decision);
    }

    [Fact]
    public void BeginClose_AfterFailedFlushAllowsAnotherSaveAttempt()
    {
        WindowShutdownState state = new();
        _ = state.BeginClose();
        state.FailFlush();

        WindowShutdownDecision decision = state.BeginClose();

        Assert.Equal(WindowShutdownDecision.CancelAndFlush, decision);
    }
}
