using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Windowing;

public sealed class WindowVisibilityCoordinatorTests
{
    [Fact]
    public void UserHidden_WhenFullscreenExits_RemainsHidden()
    {
        var coordinator = new WindowVisibilityCoordinator();

        coordinator.SetUserRequestedVisibility(false);
        coordinator.SetFullscreenPolicyVisibility(false);
        coordinator.SetFullscreenPolicyVisibility(true);

        Assert.False(coordinator.State.IsUserRequestedVisible);
        Assert.True(coordinator.State.IsFullscreenPolicyVisible);
        Assert.False(coordinator.State.IsActuallyVisible);
    }

    [Fact]
    public void UserVisible_WhileFullscreenActive_IsHiddenThenRestored()
    {
        var coordinator = new WindowVisibilityCoordinator();

        coordinator.SetFullscreenPolicyVisibility(false);

        Assert.True(coordinator.State.IsUserRequestedVisible);
        Assert.False(coordinator.State.IsActuallyVisible);

        coordinator.SetFullscreenPolicyVisibility(true);

        Assert.True(coordinator.State.IsUserRequestedVisible);
        Assert.True(coordinator.State.IsActuallyVisible);
    }

    [Fact]
    public void TrayAndHotKeyToggles_CanShareSameCoordinatorInstance()
    {
        var coordinator = new WindowVisibilityCoordinator();
        Action trayToggle = () => coordinator.ToggleUserRequestedVisibility();
        Action hotKeyToggle = () => coordinator.ToggleUserRequestedVisibility();

        trayToggle();
        Assert.False(coordinator.State.IsActuallyVisible);

        hotKeyToggle();
        Assert.True(coordinator.State.IsActuallyVisible);
    }

    [Fact]
    public void WindowClosing_HidesUnlessExitWasRequested()
    {
        var coordinator = new WindowVisibilityCoordinator();

        Assert.True(coordinator.HandleWindowClosing());
        Assert.False(coordinator.State.IsUserRequestedVisible);

        coordinator.BeginExit();

        Assert.False(coordinator.HandleWindowClosing());
        Assert.True(coordinator.State.IsExiting);
    }

    [Fact]
    public void RepeatedVisibilityRequests_DoNotPublishOrCreateNewCoordinator()
    {
        var coordinator = new WindowVisibilityCoordinator();
        int stateChanges = 0;
        coordinator.StateChanged += _ => stateChanges++;

        coordinator.SetUserRequestedVisibility(false);
        coordinator.SetUserRequestedVisibility(false);
        coordinator.SetUserRequestedVisibility(true);
        coordinator.SetUserRequestedVisibility(true);

        Assert.Equal(2, stateChanges);
        Assert.True(coordinator.State.IsActuallyVisible);
    }

    [Fact]
    public void Dispose_WhenRepeated_IsSafe()
    {
        var coordinator = new WindowVisibilityCoordinator();

        coordinator.Dispose();
        Exception? exception = Record.Exception(coordinator.Dispose);

        Assert.Null(exception);
    }
}
