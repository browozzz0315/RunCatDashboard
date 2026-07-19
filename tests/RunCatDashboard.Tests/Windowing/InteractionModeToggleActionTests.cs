using RunCatDashboard.App.Services;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Windowing;

public sealed class InteractionModeToggleActionTests
{
    [Fact]
    public void RequestToggle_DispatchesOnceAndPublishesConfirmedState()
    {
        var dispatcher = new RecordingDispatcher();
        var coordinator = new FakeModeCoordinator();
        var action = new InteractionModeToggleAction(dispatcher, coordinator);
        var published = new List<OverlayWindowState>();
        action.StateChanged += published.Add;

        action.RequestToggle();

        Assert.Equal(1, dispatcher.InvocationCount);
        Assert.Equal(1, coordinator.ToggleCount);
        OverlayWindowState state = Assert.Single(published);
        Assert.Equal(OverlayInteractionMode.Interactive, state.RequestedMode);
        Assert.Equal(OverlayInteractionMode.Interactive, state.AppliedMode);
    }

    [Fact]
    public void RequestToggle_WhenNativeApplicationFails_PublishesFaultWithoutFalseSuccess()
    {
        var dispatcher = new RecordingDispatcher();
        var coordinator = new FakeModeCoordinator { FailToggle = true };
        var action = new InteractionModeToggleAction(dispatcher, coordinator);
        OverlayWindowState? published = null;
        action.StateChanged += state => published = state;

        action.RequestToggle();

        Assert.NotNull(published);
        Assert.Equal(OverlayInteractionMode.Interactive, published.RequestedMode);
        Assert.Equal(OverlayInteractionMode.ClickThrough, published.AppliedMode);
        Assert.Equal("configured mode failure", published.LastError);
        Assert.Equal(1, dispatcher.InvocationCount);
        Assert.Equal(1, coordinator.ToggleCount);
    }

    private sealed class RecordingDispatcher : IUiDispatcher
    {
        internal int InvocationCount { get; private set; }

        public ValueTask InvokeAsync(
            Action action,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            action();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeModeCoordinator : IOverlayModeCoordinator
    {
        public OverlayWindowState State { get; private set; } = new(
            OverlayInteractionMode.ClickThrough,
            OverlayInteractionMode.ClickThrough,
            true,
            false,
            null);
        internal int ToggleCount { get; private set; }
        internal bool FailToggle { get; init; }

        public OverlayWindowState TrySetMode(OverlayInteractionMode mode) => State;

        public OverlayWindowState ToggleMode()
        {
            ToggleCount++;
            State = FailToggle
                ? State with
                {
                    RequestedMode = OverlayInteractionMode.Interactive,
                    LastError = "configured mode failure"
                }
                : State with
                {
                    RequestedMode = OverlayInteractionMode.Interactive,
                    AppliedMode = OverlayInteractionMode.Interactive,
                    LastError = null
                };
            return State;
        }
    }
}
