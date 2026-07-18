namespace RunCatDashboard.App.Windowing;

internal sealed class OverlayModeCoordinator : IOverlayModeCoordinator
{
    private readonly IOverlayWindowController _controller;

    internal OverlayModeCoordinator(IOverlayWindowController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller = controller;
    }

    public OverlayWindowState State => _controller.State;

    public OverlayWindowState TrySetMode(OverlayInteractionMode mode)
    {
        try
        {
            _controller.SetMode(mode);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
        }

        return _controller.State;
    }

    public OverlayWindowState ToggleMode()
    {
        OverlayWindowState current = _controller.State;
        OverlayInteractionMode basis = current.AppliedMode ?? current.RequestedMode;
        OverlayInteractionMode target = basis == OverlayInteractionMode.ClickThrough
            ? OverlayInteractionMode.Interactive
            : OverlayInteractionMode.ClickThrough;

        return TrySetMode(target);
    }
}
