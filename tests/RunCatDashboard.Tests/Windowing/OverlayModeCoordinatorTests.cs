using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Windowing;

public sealed class OverlayModeCoordinatorTests
{
    [Fact]
    public void ToggleMode_UsesConfirmedAppliedModeAndReturnsUpdatedState()
    {
        var controller = new FakeOverlayWindowController();
        var coordinator = new OverlayModeCoordinator(controller);

        OverlayWindowState result = coordinator.ToggleMode();

        Assert.Equal(1, controller.SetModeCount);
        Assert.Equal(OverlayInteractionMode.Interactive, result.RequestedMode);
        Assert.Equal(OverlayInteractionMode.Interactive, result.AppliedMode);
    }

    [Fact]
    public void TrySetMode_WhenControllerReportsFailure_ReturnsDiagnosticState()
    {
        var controller = new FakeOverlayWindowController
        {
            ThrowOnSet = true
        };
        var coordinator = new OverlayModeCoordinator(controller);

        OverlayWindowState result = coordinator.TrySetMode(OverlayInteractionMode.Interactive);

        Assert.Equal(OverlayInteractionMode.ClickThrough, result.AppliedMode);
        Assert.Equal("configured mode failure", result.LastError);
    }

    private sealed class FakeOverlayWindowController : IOverlayWindowController
    {
        public OverlayWindowState State { get; private set; } = new(
            OverlayInteractionMode.ClickThrough,
            OverlayInteractionMode.ClickThrough,
            true,
            false,
            null);

        public bool IsInitialized => State.IsInitialized;

        internal int SetModeCount { get; private set; }

        internal bool ThrowOnSet { get; init; }

        public void Initialize(nint windowHandle)
        {
        }

        public bool SetMode(OverlayInteractionMode mode)
        {
            SetModeCount++;
            if (ThrowOnSet)
            {
                State = State with
                {
                    RequestedMode = mode,
                    LastError = "configured mode failure"
                };
                throw new InvalidOperationException(State.LastError);
            }

            State = new OverlayWindowState(mode, mode, true, false, null);
            return true;
        }

        public void Close()
        {
        }
    }
}
