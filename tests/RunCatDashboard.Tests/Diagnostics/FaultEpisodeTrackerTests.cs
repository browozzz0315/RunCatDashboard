using RunCatDashboard.App.Diagnostics;

namespace RunCatDashboard.Tests.Diagnostics;

public sealed class FaultEpisodeTrackerTests
{
    [Fact]
    public void Observe_ReportsOnlyFirstFailureAndRecovery()
    {
        var tracker = new FaultEpisodeTracker();

        Assert.Equal(FaultEpisodeTransition.None, tracker.Observe(false));
        Assert.Equal(FaultEpisodeTransition.Failed, tracker.Observe(true));
        Assert.Equal(FaultEpisodeTransition.None, tracker.Observe(true));
        Assert.Equal(FaultEpisodeTransition.Recovered, tracker.Observe(false));
        Assert.Equal(FaultEpisodeTransition.None, tracker.Observe(false));
    }
}
