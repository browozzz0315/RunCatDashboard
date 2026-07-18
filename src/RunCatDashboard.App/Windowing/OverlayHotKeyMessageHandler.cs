namespace RunCatDashboard.App.Windowing;

internal sealed class OverlayHotKeyMessageHandler : IOverlayHotKeyMessageHandler
{
    private readonly IGlobalHotKeyController _hotKeyController;
    private readonly IOverlayModeCoordinator _modeCoordinator;

    internal OverlayHotKeyMessageHandler(
        IGlobalHotKeyController hotKeyController,
        IOverlayModeCoordinator modeCoordinator)
    {
        ArgumentNullException.ThrowIfNull(hotKeyController);
        ArgumentNullException.ThrowIfNull(modeCoordinator);
        _hotKeyController = hotKeyController;
        _modeCoordinator = modeCoordinator;
    }

    public bool TryHandleMessage(
        int message,
        nint parameter,
        out OverlayWindowState overlayState)
    {
        if (!_hotKeyController.IsTargetMessage(message, parameter))
        {
            overlayState = _modeCoordinator.State;
            return false;
        }

        overlayState = _modeCoordinator.ToggleMode();
        return true;
    }
}
