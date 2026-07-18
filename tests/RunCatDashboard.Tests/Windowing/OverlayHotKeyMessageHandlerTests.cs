using RunCatDashboard.App.Interop;
using RunCatDashboard.App.Windowing;

namespace RunCatDashboard.Tests.Windowing;

public sealed class OverlayHotKeyMessageHandlerTests
{
    [Fact]
    public void TryHandleMessage_WithRegisteredTargetHotKey_TogglesMode()
    {
        var hotKeyController = new GlobalHotKeyController(new SuccessfulNativeHotKeyApi());
        hotKeyController.Register(new nint(1234));
        var coordinator = new FakeOverlayModeCoordinator();
        var handler = new OverlayHotKeyMessageHandler(hotKeyController, coordinator);

        bool handled = handler.TryHandleMessage(
            GlobalHotKeyController.WindowMessageHotKey,
            new nint(GlobalHotKeyController.HotKeyIdentifier),
            out OverlayWindowState state);

        Assert.True(handled);
        Assert.Equal(1, coordinator.ToggleCount);
        Assert.Equal(OverlayInteractionMode.Interactive, state.AppliedMode);
    }

    [Theory]
    [InlineData(0x000F, GlobalHotKeyController.HotKeyIdentifier)]
    [InlineData(GlobalHotKeyController.WindowMessageHotKey, 999)]
    public void TryHandleMessage_WithNonTargetMessage_DoesNothing(
        int message,
        int parameter)
    {
        var hotKeyController = new GlobalHotKeyController(new SuccessfulNativeHotKeyApi());
        hotKeyController.Register(new nint(1234));
        var coordinator = new FakeOverlayModeCoordinator();
        var handler = new OverlayHotKeyMessageHandler(hotKeyController, coordinator);

        bool handled = handler.TryHandleMessage(
            message,
            new nint(parameter),
            out OverlayWindowState state);

        Assert.False(handled);
        Assert.Equal(0, coordinator.ToggleCount);
        Assert.Equal(OverlayInteractionMode.ClickThrough, state.AppliedMode);
    }

    private sealed class SuccessfulNativeHotKeyApi : INativeGlobalHotKeyApi
    {
        public void Register(nint windowHandle, int identifier, uint modifiers, uint virtualKey)
        {
        }

        public void Unregister(nint windowHandle, int identifier)
        {
        }
    }

    private sealed class FakeOverlayModeCoordinator : IOverlayModeCoordinator
    {
        public OverlayWindowState State { get; private set; } = new(
            OverlayInteractionMode.ClickThrough,
            OverlayInteractionMode.ClickThrough,
            true,
            false,
            null);

        internal int ToggleCount { get; private set; }

        public OverlayWindowState TrySetMode(OverlayInteractionMode mode)
        {
            State = new OverlayWindowState(mode, mode, true, false, null);
            return State;
        }

        public OverlayWindowState ToggleMode()
        {
            ToggleCount++;
            OverlayInteractionMode target = State.AppliedMode == OverlayInteractionMode.ClickThrough
                ? OverlayInteractionMode.Interactive
                : OverlayInteractionMode.ClickThrough;
            return TrySetMode(target);
        }
    }
}
