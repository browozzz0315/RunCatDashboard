using RunCatDashboard.App.Interop;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Windowing;

public sealed class OverlayHotKeyMessageHandlerTests
{
    [Theory]
    [InlineData(GlobalHotKeyController.InteractionHotKeyIdentifier, true, false)]
    [InlineData(GlobalHotKeyController.VisibilityHotKeyIdentifier, false, true)]
    public void TryHandleMessage_DispatchesToCorrectCoordinator(
        int identifier,
        bool togglesMode,
        bool togglesVisibility)
    {
        var hotKeys = new GlobalHotKeyController(new SuccessfulNativeHotKeyApi());
        hotKeys.RegisterAll(new nint(1234));
        var interactionAction = new FakeInteractionModeToggleAction();
        var visibility = new WindowVisibilityCoordinator();
        var handler = new OverlayHotKeyMessageHandler(
            hotKeys,
            interactionAction,
            visibility);

        bool handled = handler.TryHandleMessage(
            GlobalHotKeyController.WindowMessageHotKey,
            new nint(identifier));

        Assert.True(handled);
        Assert.Equal(togglesMode ? 1 : 0, interactionAction.RequestCount);
        Assert.Equal(!togglesVisibility, visibility.State.IsUserRequestedVisible);
    }

    [Fact]
    public void TryHandleMessage_WithNonTargetMessage_DoesNothing()
    {
        var hotKeys = new GlobalHotKeyController(new SuccessfulNativeHotKeyApi());
        hotKeys.RegisterAll(new nint(1234));
        var interactionAction = new FakeInteractionModeToggleAction();
        var visibility = new WindowVisibilityCoordinator();
        var handler = new OverlayHotKeyMessageHandler(
            hotKeys,
            interactionAction,
            visibility);

        Assert.False(handler.TryHandleMessage(0x000F, new nint(999)));
        Assert.Equal(0, interactionAction.RequestCount);
        Assert.True(visibility.State.IsUserRequestedVisible);
    }

    private sealed class SuccessfulNativeHotKeyApi : INativeGlobalHotKeyApi
    {
        public void Register(nint windowHandle, int identifier, uint modifiers, uint virtualKey) { }
        public void Unregister(nint windowHandle, int identifier) { }
    }

    private sealed class FakeInteractionModeToggleAction : IInteractionModeToggleAction
    {
        public OverlayWindowState State { get; private set; } = new(
            OverlayInteractionMode.ClickThrough,
            OverlayInteractionMode.ClickThrough,
            true,
            false,
            null);
        public event Action<OverlayWindowState>? StateChanged;
        internal int RequestCount { get; private set; }
        public void RequestToggle()
        {
            RequestCount++;
            OverlayInteractionMode mode = State.AppliedMode == OverlayInteractionMode.ClickThrough
                ? OverlayInteractionMode.Interactive
                : OverlayInteractionMode.ClickThrough;
            State = State with { RequestedMode = mode, AppliedMode = mode };
            StateChanged?.Invoke(State);
        }
    }
}
