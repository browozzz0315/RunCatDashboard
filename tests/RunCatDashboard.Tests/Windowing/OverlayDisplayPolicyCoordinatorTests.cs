using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Windowing;

public sealed class OverlayDisplayPolicyCoordinatorTests
{
    [Theory]
    [InlineData(OverlayDisplayPolicy.AlwaysOnTop, true, true, true, true)]
    [InlineData(OverlayDisplayPolicy.HideOverFullscreenApps, true, true, false, true)]
    [InlineData(OverlayDisplayPolicy.HideOverFullscreenApps, true, false, true, true)]
    [InlineData(OverlayDisplayPolicy.HideOverFullscreenApps, false, true, true, true)]
    [InlineData(OverlayDisplayPolicy.NeverTopmost, true, true, true, false)]
    public void Calculate_ImplementsPolicyMatrix(
        OverlayDisplayPolicy policy,
        bool fullscreen,
        bool sameMonitor,
        bool expectedVisible,
        bool expectedTopmost)
    {
        FullscreenObservation observation = Observation(fullscreen, sameMonitor);

        OverlayDisplayPolicyState result =
            OverlayDisplayPolicyCoordinator.Calculate(policy, observation);

        Assert.Equal(expectedVisible, result.IsVisible);
        Assert.Equal(expectedTopmost, result.IsTopmost);
    }

    [Fact]
    public void Calculate_WithDetectorFault_IsFailVisible()
    {
        FullscreenObservation observation = Observation(true, true) with
        {
            Fault = "configured detector failure"
        };

        OverlayDisplayPolicyState result = OverlayDisplayPolicyCoordinator.Calculate(
            OverlayDisplayPolicy.HideOverFullscreenApps,
            observation);

        Assert.True(result.IsVisible);
        Assert.True(result.IsTopmost);
        Assert.Equal("configured detector failure", result.Fault);
    }

    [Fact]
    public void SetPolicy_RecalculatesImmediatelyFromLatestObservation()
    {
        var coordinator = new OverlayDisplayPolicyCoordinator();
        coordinator.UpdateObservation(Observation(true, true));

        OverlayDisplayPolicyState hidden = coordinator.State;
        OverlayDisplayPolicyState visible = coordinator.SetPolicy(
            OverlayDisplayPolicy.AlwaysOnTop);

        Assert.False(hidden.IsVisible);
        Assert.True(visible.IsVisible);
        Assert.True(visible.IsTopmost);
    }

    private static FullscreenObservation Observation(bool fullscreen, bool sameMonitor) =>
        new(
            fullscreen,
            sameMonitor,
            "foreground diagnostic",
            "overlay diagnostic",
            null);
}
